using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Artifacts;

/// <summary>What an artifact is, detected from its extension and leading bytes; picks the viewer.</summary>
public enum ArtifactKind
{
    /// <summary>PNG, JPEG, GIF or WebP.</summary>
    Image,

    Svg,

    /// <summary>MP4 or WebM.</summary>
    Video,

    /// <summary>STL or 3MF.</summary>
    Model3d,

    /// <summary>Source text with a known extension.</summary>
    Code,

    /// <summary><c>.diff</c> or <c>.patch</c>.</summary>
    Diff,

    Pdf,

    Markdown,

    Html,

    /// <summary>Anything else, including content that does not match its extension; served as a download.</summary>
    Other,
}

/// <summary>Where an artifact came from.</summary>
public enum ArtifactSource
{
    /// <summary>Uploaded by the user in the web UI or API.</summary>
    Upload,

    /// <summary>Written by a coding executor session (tainted).</summary>
    Executor,

    /// <summary>A kept printer snapshot.</summary>
    Printer,

    /// <summary>A saved mail attachment (tainted).</summary>
    Mail,

    /// <summary>A pull request diff or CI log from a <c>repos.*</c> tool (tainted); stored and serialized as <c>github</c>.</summary>
    Github,
}

/// <summary>A stored artifact.</summary>
/// <param name="Id">Artifact id.</param>
/// <param name="OwnerId">Owner.</param>
/// <param name="Name">File name as given (no path).</param>
/// <param name="Kind">Detected kind.</param>
/// <param name="MediaType">Detected media type.</param>
/// <param name="Size">Size in bytes.</param>
/// <param name="Sha256">Lowercase hex SHA-256 of the content.</param>
/// <param name="Source">Where it came from.</param>
/// <param name="SourceReference">Task id, message reference, printer snapshot or repository item, depending on the source.</param>
/// <param name="Tainted">Content or name can carry third-party text (executor output, mail).</param>
/// <param name="CreatedAt">When it was stored.</param>
public sealed record ArtifactRecord(
    ArtifactId Id,
    OwnerId OwnerId,
    string Name,
    ArtifactKind Kind,
    string MediaType,
    long Size,
    string Sha256,
    ArtifactSource Source,
    string? SourceReference,
    bool Tainted,
    DateTimeOffset CreatedAt);

/// <summary>A new artifact to store; kind and media type are detected from the name and content.</summary>
public sealed record ArtifactDraft(string Name, ArtifactSource Source, string? SourceReference = null, bool Tainted = false);

/// <summary>Filter for listing artifacts, newest first.</summary>
public sealed record ArtifactQuery(ArtifactKind? Kind = null, ArtifactSource? Source = null, string? NameContains = null, int Limit = 200);

/// <summary>Storage used and allowed for the current owner, in bytes.</summary>
public sealed record ArtifactUsage(long Used, long Quota);

/// <summary>An artifact as a turn shows it: enough for the chat to render a card or inline image.</summary>
public sealed record ArtifactRef(ArtifactId Id, string Name, ArtifactKind Kind, string Url);
