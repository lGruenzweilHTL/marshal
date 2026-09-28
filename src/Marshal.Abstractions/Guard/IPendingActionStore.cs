using Marshal.Abstractions.Sessions;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// Persists frozen pending actions for the current owner. At most one pending action per session.
/// </summary>
public interface IPendingActionStore
{
    /// <summary>Stores a new pending action with a fresh nonce; a pending action of the same session is superseded.</summary>
    Task<PendingActionRecord> FreezeAsync(FreezeRequest request, CancellationToken cancellationToken);

    /// <summary>The pending action of a session (status pending, expired or not), or null.</summary>
    Task<PendingActionRecord?> GetPendingAsync(SessionId sessionId, CancellationToken cancellationToken);

    /// <summary>Moves a pending action to a final status. Fails with <c>pending_action.not_pending</c> when already decided.</summary>
    Task<Result> DecideAsync(PendingActionId id, PendingActionStatus status, DecidedBy decidedBy, CancellationToken cancellationToken);

    /// <summary>Drops the pending action of a session (e.g. on idle close).</summary>
    Task DropForSessionAsync(SessionId sessionId, CancellationToken cancellationToken);
}
