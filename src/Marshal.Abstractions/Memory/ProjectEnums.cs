namespace Marshal.Abstractions.Memory;

/// <summary>Status of a project alias.</summary>
public enum ProjectAliasStatus
{
    /// <summary>Led to an uncorrected inference; promoted after enough confirmations.</summary>
    Candidate,

    /// <summary>Activates the project when the turn contains it.</summary>
    Active,

    Rejected,
}

/// <summary>Kind of project feedback.</summary>
public enum ProjectFeedbackKind
{
    Confirmed,
    Rejected,
    Switched,
}
