using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;

namespace Marshal.Data.Tasks;

/// <summary>
/// A change waiting for review in the web UI. Merging is a user action only.
/// </summary>
public sealed class Proposal : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public TaskId? TaskId { get; init; }

    /// <summary>The executor session that produced the change; merging and discarding go through its executor.</summary>
    public Guid? ExecutorSessionId { get; init; }

    public required ProposalKind Kind { get; init; }

    /// <summary>Repository or sink the change applies to.</summary>
    public required string Target { get; init; }

    public required string Branch { get; init; }

    public required string Title { get; init; }

    /// <summary>Unified diff of the change.</summary>
    public required string Diff { get; init; }

    public ProposalStatus Status { get; set; } = ProposalStatus.Open;

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? DecidedAt { get; set; }
}
