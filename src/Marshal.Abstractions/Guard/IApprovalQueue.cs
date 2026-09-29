#pragma warning disable CA1711 // "Queue" is the domain term (the review queue), not a collection type.

namespace Marshal.Abstractions.Guard;

/// <summary>
/// Confirmations of background runs: frozen with a longer expiry, sent to the owner as an actionable push and shown
/// in the review queue; the decision resumes the waiting task.
/// </summary>
public interface IApprovalQueue
{
    /// <summary>Freezes a confirm-tier call of a background task run and notifies the owner.</summary>
    Task<PendingActionRecord> EnqueueAsync(FreezeRequest request, CancellationToken cancellationToken);

    /// <summary>The current owner's undecided approvals, oldest first.</summary>
    Task<IReadOnlyList<PendingActionRecord>> ListPendingAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Approves (executes exactly the frozen action) or rejects an approval of the current owner and resumes its
    /// task with the decision. Fails when it is unknown, already decided or expired.
    /// </summary>
    Task<Result<PendingActionRecord>> DecideAsync(PendingActionId id, bool approve, DecidedBy decidedBy, CancellationToken cancellationToken);
}
