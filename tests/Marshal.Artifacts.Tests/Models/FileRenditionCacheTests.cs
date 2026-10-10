using Marshal.Artifacts.Models;

namespace Marshal.Artifacts.Tests.Models;

public sealed class FileRenditionCacheTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-cache-").FullName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public async Task SetThenGet_SameBytes_MissingIsNull()
    {
        var cache = new FileRenditionCache(_directory);

        await cache.SetAsync("abc-p1", [1, 2, 3], Ct);

        Assert.Equal([1, 2, 3], await cache.GetAsync("abc-p1", Ct));
        Assert.Null(await cache.GetAsync("abc-p2", Ct));
    }

    [Fact]
    public async Task OverCap_OldestAccessEvicted()
    {
        var cache = new FileRenditionCache(_directory, maxBytes: 250);
        await cache.SetAsync("a", new byte[100], Ct);
        File.SetLastAccessTimeUtc(Path.Combine(_directory, "a.png"), DateTime.UtcNow.AddHours(-2));
        await cache.SetAsync("b", new byte[100], Ct);
        File.SetLastAccessTimeUtc(Path.Combine(_directory, "b.png"), DateTime.UtcNow.AddHours(-1));

        await cache.SetAsync("c", new byte[100], Ct);

        Assert.Null(await cache.GetAsync("a", Ct));
        Assert.NotNull(await cache.GetAsync("b", Ct));
        Assert.NotNull(await cache.GetAsync("c", Ct));
    }

    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("")]
    public async Task UnsafeKey_Rejected(string key)
    {
        var cache = new FileRenditionCache(_directory);

        await Assert.ThrowsAsync<ArgumentException>(() => cache.SetAsync(key, [1], Ct));
    }
}
