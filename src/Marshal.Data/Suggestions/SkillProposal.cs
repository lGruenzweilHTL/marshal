using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Suggestions;

namespace Marshal.Data.Suggestions;

/// <summary>
/// A drafted <c>SKILL.md</c> for a workflow the owner repeated, or a record that the workflow is already covered by a
/// skill; either way the same tool sequence is not drafted again.
/// </summary>
public sealed class SkillProposal : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    /// <summary>Skill name from the draft's frontmatter; empty for a covered workflow.</summary>
    public required string Name { get; init; }

    public required string Description { get; init; }

    /// <summary>The full <c>SKILL.md</c> text; empty for a covered workflow.</summary>
    public required string Content { get; init; }

    /// <summary>Unified diff against the existing skill of the same name; null when there is none.</summary>
    public string? Diff { get; init; }

    /// <summary>JSON array of the source run ids.</summary>
    public required string SourceRunIds { get; init; }

    /// <summary>Hash of the tool id sequence; one row per sequence and owner.</summary>
    public required string SequenceHash { get; init; }

    public SkillProposalStatus Status { get; set; } = SkillProposalStatus.Open;

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; set; }
}
