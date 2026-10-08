using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Identity;

namespace Marshal.Data.Artifacts;

/// <summary>An artifact row; the content is the file <c>/data/artifacts/&lt;owner&gt;/&lt;id&gt;</c>.</summary>
public sealed class StoredArtifact : IOwned
{
    public required ArtifactId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required string Name { get; init; }

    public required ArtifactKind Kind { get; init; }

    public required string MediaType { get; init; }

    public required long Size { get; init; }

    /// <summary>Lowercase hex SHA-256 of the content.</summary>
    public required string Sha256 { get; init; }

    public required ArtifactSource Source { get; init; }

    /// <summary>Task id, message reference or printer snapshot, depending on the source.</summary>
    public string? SourceReference { get; init; }

    public required bool Tainted { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
