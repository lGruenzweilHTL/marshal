using Marshal.Abstractions.Displays;

namespace Marshal.Abstractions.Artifacts;

/// <summary>
/// Media renditions a renderer (a TV) can play: images and MP4 as stored or converted, text kinds as PNG pages.
/// Renditions are produced on request and never stored. Reads go through the owner-scoped artifact store.
/// </summary>
public interface IArtifactRenditions
{
    /// <summary>The artifact's rendition; fails with <c>artifacts.no_rendition</c> for kinds without one.</summary>
    Task<Result<ArtifactRendition>> DescribeAsync(ArtifactId id, CancellationToken cancellationToken);

    /// <summary>Page <paramref name="page"/> (1-based) of the rendition; the caller disposes the stream.</summary>
    Task<Result<Stream>> OpenAsync(ArtifactId id, int page, CancellationToken cancellationToken);
}

/// <summary>Shape of an artifact's media rendition.</summary>
/// <param name="Artifact">The artifact.</param>
/// <param name="Pages">Number of pages (1 for images and video).</param>
/// <param name="MediaType">Media type of every page.</param>
/// <param name="Extension">File extension without dot (<c>png</c>, <c>jpg</c>, <c>mp4</c>).</param>
/// <param name="DisplayType">Whether a renderer shows it as image or video.</param>
public sealed record ArtifactRendition(ArtifactRecord Artifact, int Pages, string MediaType, string Extension, DisplayMediaType DisplayType);
