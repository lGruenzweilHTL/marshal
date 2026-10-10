namespace Marshal.Abstractions.Artifacts;

/// <summary>Small preview images for the artifact list: the shaded view of a 3D model, or a scaled image.</summary>
public interface IArtifactPreviews
{
    /// <summary>
    /// A PNG fitting 480 × 270 pixels; not found for an unknown or foreign id (<c>artifacts.not_found</c>) and for kinds
    /// without a preview or files that cannot be read (<c>artifacts.no_preview</c>).
    /// </summary>
    Task<Result<ArtifactPreview>> GetPreviewAsync(ArtifactId id, CancellationToken cancellationToken);
}

/// <summary>A preview PNG and the SHA-256 of the artifact it shows (for the ETag).</summary>
public sealed record ArtifactPreview(byte[] Png, string Sha256);
