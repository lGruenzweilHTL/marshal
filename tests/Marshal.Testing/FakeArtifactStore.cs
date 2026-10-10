using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;

namespace Marshal.Testing;

/// <summary>Artifacts in a list (one owner); the kind comes from the extension only.</summary>
public sealed class FakeArtifactStore : IArtifactStore
{
    public List<(ArtifactRecord Record, byte[] Content)> Artifacts { get; } = [];

    /// <summary>When set, every create fails with it (e.g. quota exceeded).</summary>
    public Error? Failure { get; set; }

    public async Task<Result<ArtifactRecord>> CreateAsync(ArtifactDraft draft, Stream content, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
            return failure;

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, cancellationToken);
        (ArtifactKind kind, string mediaType) = Path.GetExtension(draft.Name).ToLowerInvariant() switch
        {
            ".pdf" => (ArtifactKind.Pdf, "application/pdf"),
            ".png" => (ArtifactKind.Image, "image/png"),
            ".jpg" => (ArtifactKind.Image, "image/jpeg"),
            ".gif" => (ArtifactKind.Image, "image/gif"),
            ".webp" => (ArtifactKind.Image, "image/webp"),
            ".mp4" => (ArtifactKind.Video, "video/mp4"),
            ".webm" => (ArtifactKind.Video, "video/webm"),
            ".svg" => (ArtifactKind.Svg, "image/svg+xml"),
            ".cs" or ".log" => (ArtifactKind.Code, "text/plain; charset=utf-8"),
            ".diff" => (ArtifactKind.Diff, "text/x-diff; charset=utf-8"),
            ".stl" => (ArtifactKind.Model3d, "model/stl"),
            ".3mf" => (ArtifactKind.Model3d, "model/3mf"),
            ".md" => (ArtifactKind.Markdown, "text/markdown; charset=utf-8"),
            _ => (ArtifactKind.Other, "application/octet-stream"),
        };
        var record = new ArtifactRecord(ArtifactId.New(), TestExecutionContext.OwnerA, draft.Name, kind, mediaType, buffer.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(buffer.ToArray())), draft.Source, draft.SourceReference, draft.Tainted, DateTimeOffset.UnixEpoch);
        Artifacts.Add((record, buffer.ToArray()));
        return record;
    }

    public Task<Result<ArtifactRecord>> GetAsync(ArtifactId id, CancellationToken cancellationToken) =>
        Task.FromResult(Find(id) is { } found ? Result.Success(found.Record) : Result.Failure<ArtifactRecord>(NotFound(id)));

    public Task<Result<(ArtifactRecord Artifact, Stream Content)>> OpenReadAsync(ArtifactId id, CancellationToken cancellationToken) =>
        Task.FromResult(Find(id) is { } found
            ? Result.Success<(ArtifactRecord, Stream)>((found.Record, new MemoryStream(found.Content)))
            : Result.Failure<(ArtifactRecord, Stream)>(NotFound(id)));

    public Task<IReadOnlyList<ArtifactRecord>> ListAsync(ArtifactQuery query, CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<ArtifactRecord>>([.. Artifacts.Select(entry => entry.Record).Reverse().Take(query.Limit)]);

    public Task<ArtifactRecord?> FindBySourceAsync(ArtifactSource source, string reference, CancellationToken cancellationToken) =>
        Task.FromResult(Artifacts.Select(entry => entry.Record).LastOrDefault(record => record.Source == source && record.SourceReference == reference));

    public Task<Result> DeleteAsync(ArtifactId id, CancellationToken cancellationToken) =>
        Task.FromResult(Artifacts.RemoveAll(entry => entry.Record.Id == id) > 0 ? Result.Success() : Result.Failure(NotFound(id)));

    public Task<ArtifactUsage> UsageAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ArtifactUsage(Artifacts.Sum(entry => entry.Record.Size), long.MaxValue));

    private (ArtifactRecord Record, byte[] Content)? Find(ArtifactId id) =>
        Artifacts.FirstOrDefault(entry => entry.Record.Id == id) is { Record: not null } found ? found : null;

    private static Error NotFound(ArtifactId id) => new("artifacts.not_found", $"There is no artifact '{id}'.", ErrorType.NotFound);
}
