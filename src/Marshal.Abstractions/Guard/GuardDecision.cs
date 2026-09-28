namespace Marshal.Abstractions.Guard;

/// <summary>
/// Guard decision recorded on a tool-call run step.
/// </summary>
public enum GuardDecision
{
    /// <summary>Allowed and executed.</summary>
    Safe,

    /// <summary>Frozen as a pending action.</summary>
    Pending,

    /// <summary>Executed after an exact keyword confirmation.</summary>
    ConfirmedByKeyword,

    /// <summary>Executed after the judge answered positive.</summary>
    JudgePositive,

    /// <summary>Dropped because the judge answered neutral (or failed).</summary>
    JudgeNeutral,

    /// <summary>Dropped because the judge answered negative.</summary>
    JudgeNegative,

    /// <summary>Raised from safe to confirm because the run is tainted.</summary>
    EscalatedByTaint,

    /// <summary>Denied; nothing executed.</summary>
    Denied,

    /// <summary>The pending action expired before confirmation.</summary>
    Expired,
}
