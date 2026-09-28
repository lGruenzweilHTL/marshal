namespace Marshal.Abstractions.Guard;

/// <summary>
/// Lifecycle state of a frozen pending action.
/// </summary>
public enum PendingActionStatus
{
    /// <summary>Waiting for a user decision.</summary>
    Pending,

    /// <summary>Confirmed and executed.</summary>
    Approved,

    /// <summary>Rejected by the user.</summary>
    Rejected,

    /// <summary>Dropped without execution (topic change, judge failure, session closed).</summary>
    Dropped,

    /// <summary>Not confirmed before the expiry.</summary>
    Expired,

    /// <summary>Replaced by a newer pending action in the same session.</summary>
    Superseded,
}
