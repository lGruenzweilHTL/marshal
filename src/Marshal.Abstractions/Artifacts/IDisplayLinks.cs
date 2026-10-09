namespace Marshal.Abstractions.Artifacts;

/// <summary>Signed, unauthenticated links to rendition pages that displays fetch.</summary>
public interface IDisplayLinks
{
    /// <summary>
    /// Absolute URL under the media base URL, bound to the current owner, <paramref name="id"/> and
    /// <paramref name="page"/>, ending in <c>.<paramref name="extension"/></c>, expiring after <c>displays.linkMinutes</c>.
    /// </summary>
    string Create(ArtifactId id, int page, string extension);
}
