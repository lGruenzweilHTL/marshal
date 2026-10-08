namespace Marshal.Abstractions.Artifacts;

/// <summary>URLs of artifact content.</summary>
public interface IArtifactLinks
{
    /// <summary>Content URL on Marshal's origin; needs the owner's session or token.</summary>
    string Content(ArtifactId id);

    /// <summary>Absolute signed URL that needs no sign-in, bound to this artifact, expiring after <c>artifacts.signedLinkMinutes</c>.</summary>
    string SignedContent(ArtifactId id);
}
