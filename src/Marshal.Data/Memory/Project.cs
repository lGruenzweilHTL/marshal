using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Runs;

namespace Marshal.Data.Memory;

/// <summary>A memory project. Chunks refer to it by id, so renaming never changes chunk ids.</summary>
public sealed class Project : IOwned
{
    public required ProjectId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required string Name { get; set; }

    public string Description { get; set; } = "";

    /// <summary>Mean embedding of description and top chunks (float32 little-endian), if computed.</summary>
    public byte[]? Centroid { get; set; }

    public DateTimeOffset? LastActiveAt { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Soft-delete time; its chunks become orphans in the audit.</summary>
    public DateTimeOffset? DeletedAt { get; set; }
}

/// <summary>A phrase that names a project (active) or may do so (candidate).</summary>
public sealed class ProjectAlias : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required ProjectId ProjectId { get; init; }

    public required string Phrase { get; init; }

    public ProjectAliasStatus Status { get; set; }

    public int Confirmations { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }
}

/// <summary>A correction or confirmation of a project activation; rejected phrases are negative examples.</summary>
public sealed class ProjectFeedback : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required ProjectId ProjectId { get; init; }

    public RunId? RunId { get; init; }

    public required ProjectFeedbackKind Kind { get; init; }

    public required string Phrase { get; init; }

    /// <summary>Embedding of the phrase (float32 little-endian).</summary>
    public byte[]? Embedding { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
