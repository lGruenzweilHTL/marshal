namespace Marshal.Artifacts.Models;

/// <summary>
/// Rendered pages that take long to draw (3D models), keyed by file hash and variant. A disposable cache, never an
/// artifact: callers check the owner through the artifact store before every read.
/// </summary>
internal interface IRenditionCache
{
    Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken);

    Task SetAsync(string key, byte[] content, CancellationToken cancellationToken);
}

/// <summary>
/// PNG files in one directory (<c>/data/cache/renditions</c>), at most <c>maxBytes</c> in total; beyond it the files
/// read longest ago are deleted. Access times are set explicitly, because data volumes are often mounted noatime.
/// </summary>
internal sealed class FileRenditionCache(string directory, long maxBytes = FileRenditionCache.DefaultMaxBytes) : IRenditionCache
{
    public const long DefaultMaxBytes = 512L * 1024 * 1024;

    private readonly Lock _evicting = new();

    public async Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken)
    {
        string path = PathOf(key);
        try
        {
            byte[] content = await File.ReadAllBytesAsync(path, cancellationToken);
            File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
            return content;
        }
        catch (Exception failure) when (failure is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
    }

    public async Task SetAsync(string key, byte[] content, CancellationToken cancellationToken)
    {
        string path = PathOf(key);
        Directory.CreateDirectory(directory);
        string temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllBytesAsync(temporary, content, cancellationToken);
        File.Move(temporary, path, overwrite: true);
        File.SetLastAccessTimeUtc(path, DateTime.UtcNow);
        Evict();
    }

    private void Evict()
    {
        lock (_evicting)
        {
            FileInfo[] files = [.. new DirectoryInfo(directory).EnumerateFiles("*.png").OrderBy(file => file.LastAccessTimeUtc)];
            long total = files.Sum(file => file.Length);
            foreach (FileInfo file in files)
            {
                if (total <= maxBytes)
                    break;
                total -= file.Length;
                file.Delete();
            }
        }
    }

    private string PathOf(string key)
    {
        if (key.Length == 0 || key.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '-' or '_')))
            throw new ArgumentException($"Cache key '{key}' must be letters, digits, '-' and '_' only.", nameof(key));
        return Path.Combine(directory, key + ".png");
    }
}

/// <summary>No cache: every page is drawn again (tests, and hosts without a data directory).</summary>
internal sealed class NoRenditionCache : IRenditionCache
{
    public static NoRenditionCache Instance { get; } = new();

    public Task<byte[]?> GetAsync(string key, CancellationToken cancellationToken) => Task.FromResult<byte[]?>(null);

    public Task SetAsync(string key, byte[] content, CancellationToken cancellationToken) => Task.CompletedTask;
}
