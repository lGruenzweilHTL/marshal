using System.Collections.Concurrent;
using System.Globalization;
using System.Security.Cryptography;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.LiveEvents;
using Marshal.Data;
using Marshal.Data.Artifacts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Artifacts;

/// <summary>
/// Artifacts as files under <c>&lt;directory&gt;/&lt;owner&gt;/&lt;id&gt;</c> plus one owner-filtered row each. A write
/// streams into a temp file in the owner's folder while hashing and counting, stops as soon as the per-file limit or
/// the quota would be exceeded, detects the kind from the finished file, then moves it into place and inserts the
/// row. Writes of one owner are serialized, so two uploads cannot both pass the quota check (one Marshal instance per
/// data directory).
/// </summary>
internal sealed class FileArtifactStore(
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IArtifactKindDetector detector,
    ArtifactDirectory directory,
    IOptions<ArtifactOptions> options,
    ILiveEventPublisher live,
    TimeProvider time) : IArtifactStore
{
    /// <summary>Live-update topic: <c>artifact.created</c> (payload: the artifact) and <c>artifact.deleted</c> (payload: its id).</summary>
    public const string Topic = "artifacts";

    private const int MaxNameLength = 200;

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<Result<ArtifactRecord>> CreateAsync(ArtifactDraft draft, Stream content, CancellationToken cancellationToken)
    {
        OwnerId owner = CurrentOwner();
        string name = CleanName(draft.Name);
        if (name.Length == 0)
            return new Error("artifacts.invalid_name", "An artifact needs a file name.", ErrorType.Validation);

        string folder = OwnerFolder(owner);
        Directory.CreateDirectory(folder);
        SemaphoreSlim gate = _locks.GetOrAdd(folder, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        string temp = Path.Combine(folder, $".upload-{Guid.NewGuid():N}");
        try
        {
            ArtifactUsage usage = await UsageAsync(cancellationToken);
            long remaining = Math.Max(0, usage.Quota - usage.Used);
            bool quotaBound = remaining < options.Value.MaxFileBytes;
            (long size, string hash, bool overLimit) = await CopyAsync(content, temp, quotaBound ? remaining : options.Value.MaxFileBytes, cancellationToken);
            if (overLimit)
            {
                return quotaBound
                    ? QuotaExceeded(usage)
                    : new Error("artifacts.too_large", $"The file is larger than the limit of {options.Value.MaxUploadMb} MB.", ErrorType.Validation);
            }

            DetectedKind kind;
            await using (FileStream stored = File.OpenRead(temp))
                kind = detector.Detect(name, stored);

            var row = new StoredArtifact
            {
                Id = ArtifactId.New(),
                OwnerId = owner,
                Name = name,
                Kind = kind.Kind,
                MediaType = kind.MediaType,
                Size = size,
                Sha256 = hash,
                Source = draft.Source,
                SourceReference = draft.SourceReference,
                Tainted = draft.Tainted,
                CreatedAt = time.GetUtcNow(),
            };
            string path = Path.Combine(folder, row.Id.ToString());
            File.Move(temp, path);
            try
            {
                await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
                db.Artifacts.Add(row);
                await db.SaveChangesAsync(cancellationToken);
            }
            catch
            {
                File.Delete(path);
                throw;
            }

            ArtifactRecord created = ToRecord(row);
            live.Publish(new LiveEvent(Topic, "artifact.created", Guid.NewGuid().ToString(), owner, created));
            return created;
        }
        finally
        {
            File.Delete(temp);
            gate.Release();
        }
    }

    public async Task<Result<ArtifactRecord>> GetAsync(ArtifactId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        StoredArtifact? row = await db.Artifacts.AsNoTracking().SingleOrDefaultAsync(artifact => artifact.Id == id, cancellationToken);
        return row is null ? NotFound(id) : ToRecord(row);
    }

    public async Task<Result<(ArtifactRecord Artifact, Stream Content)>> OpenReadAsync(ArtifactId id, CancellationToken cancellationToken)
    {
        Result<ArtifactRecord> artifact = await GetAsync(id, cancellationToken);
        if (artifact.IsFailure)
            return artifact.Error;

        string path = Path.Combine(OwnerFolder(artifact.Value.OwnerId), id.ToString());
        if (!File.Exists(path))
            return NotFound(id);

        Stream content = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, useAsync: true);
        return (artifact.Value, content);
    }

    public async Task<IReadOnlyList<ArtifactRecord>> ListAsync(ArtifactQuery query, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        IQueryable<StoredArtifact> rows = db.Artifacts.AsNoTracking();
        if (query.Kind is { } kind)
            rows = rows.Where(artifact => artifact.Kind == kind);
        if (query.Source is { } source)
            rows = rows.Where(artifact => artifact.Source == source);
        if (!string.IsNullOrWhiteSpace(query.NameContains))
        {
            string pattern = $"%{query.NameContains.Trim().Replace("\\", "\\\\", StringComparison.Ordinal).Replace("%", "\\%", StringComparison.Ordinal).Replace("_", "\\_", StringComparison.Ordinal)}%";
            rows = rows.Where(artifact => EF.Functions.Like(artifact.Name, pattern, "\\"));
        }

        List<StoredArtifact> found = await rows.OrderByDescending(artifact => artifact.CreatedAt).ThenByDescending(artifact => artifact.Id)
            .Take(Math.Clamp(query.Limit, 1, 1000)).ToListAsync(cancellationToken);
        return [.. found.Select(ToRecord)];
    }

    public async Task<ArtifactRecord?> FindBySourceAsync(ArtifactSource source, string reference, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        StoredArtifact? row = await db.Artifacts.AsNoTracking()
            .Where(artifact => artifact.Source == source && artifact.SourceReference == reference)
            .OrderByDescending(artifact => artifact.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        return row is null ? null : ToRecord(row);
    }

    public async Task<Result> DeleteAsync(ArtifactId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        StoredArtifact? row = await db.Artifacts.SingleOrDefaultAsync(artifact => artifact.Id == id, cancellationToken);
        if (row is null)
            return NotFound(id);

        db.Artifacts.Remove(row);
        await db.SaveChangesAsync(cancellationToken);
        File.Delete(Path.Combine(OwnerFolder(row.OwnerId), id.ToString()));
        live.Publish(new LiveEvent(Topic, "artifact.deleted", Guid.NewGuid().ToString(), row.OwnerId, new { id = id.ToString() }));
        return Result.Success();
    }

    public async Task<ArtifactUsage> UsageAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        long used = await db.Artifacts.SumAsync(artifact => (long?)artifact.Size, cancellationToken) ?? 0;
        return new ArtifactUsage(used, options.Value.QuotaBytes);
    }

    // Copies up to limit bytes; stops at the first byte beyond it.
    private static async Task<(long Size, string Hash, bool OverLimit)> CopyAsync(Stream content, string path, long limit, CancellationToken cancellationToken)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        await using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        byte[] buffer = new byte[81920];
        long size = 0;
        int read;
        while ((read = await content.ReadAsync(buffer, cancellationToken)) > 0)
        {
            size += read;
            if (size > limit)
                return (size, "", true);
            hash.AppendData(buffer, 0, read);
            await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
        }

        return (size, Convert.ToHexStringLower(hash.GetHashAndReset()), false);
    }

    private static Error QuotaExceeded(ArtifactUsage usage) => new(
        "artifacts.quota_exceeded",
        string.Create(CultureInfo.InvariantCulture, $"The artifact quota of {Megabytes(usage.Quota)} MB is used up ({Megabytes(usage.Used)} MB used)."),
        ErrorType.Conflict);

    private static long Megabytes(long bytes) => bytes / (1024 * 1024);

    private static Error NotFound(ArtifactId id) => new("artifacts.not_found", $"There is no artifact '{id}'.", ErrorType.NotFound);

    // A bare file name: no folders, no control characters, bounded length.
    private static string CleanName(string name)
    {
        string file = Path.GetFileName(name.Replace('\\', '/').Split('/')[^1]).Trim();
        string clean = new([.. file.Where(character => !char.IsControl(character))]);
        return clean.Length > MaxNameLength ? clean[^MaxNameLength..] : clean;
    }

    private string OwnerFolder(OwnerId owner) => Path.Combine(directory.Path, owner.ToString());

    private OwnerId CurrentOwner() =>
        executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Artifacts accessed without an execution context.");

    private static ArtifactRecord ToRecord(StoredArtifact row) => new(
        row.Id, row.OwnerId, row.Name, row.Kind, row.MediaType, row.Size, row.Sha256, row.Source, row.SourceReference, row.Tainted, row.CreatedAt);
}
