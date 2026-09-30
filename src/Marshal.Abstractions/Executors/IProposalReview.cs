using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.Executors;

/// <summary>
/// The current owner's proposals in the review queue. Merging and rejecting are user actions only; each decision is
/// one conditional update from <c>open</c>, so two decisions cannot both win.
/// </summary>
public interface IProposalReview
{
    /// <summary>Proposals with <paramref name="status"/> (all when null), newest first, without their diffs.</summary>
    Task<IReadOnlyList<ProposalRecord>> ListAsync(ProposalStatus? status, CancellationToken cancellationToken);

    /// <summary>One proposal with its diff; fails with <c>proposal.not_found</c>.</summary>
    Task<Result<ProposalRecord>> GetAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Merges an open proposal through the executor that produced it. Fails with <c>proposal.decided</c>, or with the
    /// executor's error (e.g. <c>executor.merge_conflict</c>), which leaves it open.
    /// </summary>
    Task<Result<ProposalRecord>> MergeAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Rejects an open proposal and asks its executor to drop the change (best effort).</summary>
    Task<Result<ProposalRecord>> RejectAsync(Guid id, CancellationToken cancellationToken);
}

/// <summary>A proposal. <paramref name="Diff"/> is null in lists.</summary>
public sealed record ProposalRecord(
    Guid Id,
    TaskId? TaskId,
    Guid? ExecutorSessionId,
    ProposalKind Kind,
    string Target,
    string Branch,
    string Title,
    string? Diff,
    ProposalStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? DecidedAt);
