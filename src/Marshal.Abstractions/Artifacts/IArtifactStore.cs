namespace Marshal.Abstractions.Artifacts;

/// <summary>
/// Owner-scoped artifact files and rows. The owner is the execution context's owner, like every other store; another
/// owner's artifact behaves as not found.
/// </summary>
public interface IArtifactStore
{
    /// <summary>
    /// Stores <paramref name="content"/>; fails with <c>artifacts.too_large</c> over the per-file limit and
    /// <c>artifacts.quota_exceeded</c> over the owner's quota, storing nothing either way.
    /// </summary>
    Task<Result<ArtifactRecord>> CreateAsync(ArtifactDraft draft, Stream content, CancellationToken cancellationToken);

    Task<Result<ArtifactRecord>> GetAsync(ArtifactId id, CancellationToken cancellationToken);

    /// <summary>Opens the content for reading; the caller disposes the stream.</summary>
    Task<Result<(ArtifactRecord Artifact, Stream Content)>> OpenReadAsync(ArtifactId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<ArtifactRecord>> ListAsync(ArtifactQuery query, CancellationToken cancellationToken);

    /// <summary>Removes the row and the file.</summary>
    Task<Result> DeleteAsync(ArtifactId id, CancellationToken cancellationToken);

    Task<ArtifactUsage> UsageAsync(CancellationToken cancellationToken);
}
