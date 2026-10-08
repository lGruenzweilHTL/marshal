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
            ".stl" => (ArtifactKind.Model3d, "model/stl"),
            ".md" => (ArtifactKind.Markdown, "text/markdown; charset=utf-8"),
            _ => (ArtifactKind.Other, "application/octet-stream"),
        };
        var record = new ArtifactRecord(ArtifactId.New(), TestExecutionContext.OwnerA, draft.Name, kind, mediaType, buffer.Length, "", draft.Source, draft.SourceReference, draft.Tainted, DateTimeOffset.UnixEpoch);
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

    public Task<Result> DeleteAsync(ArtifactId id, CancellationToken cancellationToken) =>
        Task.FromResult(Artifacts.RemoveAll(entry => entry.Record.Id == id) > 0 ? Result.Success() : Result.Failure(NotFound(id)));

    public Task<ArtifactUsage> UsageAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ArtifactUsage(Artifacts.Sum(entry => entry.Record.Size), long.MaxValue));

    private (ArtifactRecord Record, byte[] Content)? Find(ArtifactId id) =>
        Artifacts.FirstOrDefault(entry => entry.Record.Id == id) is { Record: not null } found ? found : null;

    private static Error NotFound(ArtifactId id) => new("artifacts.not_found", $"There is no artifact '{id}'.", ErrorType.NotFound);
}
