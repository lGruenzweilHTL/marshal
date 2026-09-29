namespace Marshal.Abstractions.Executors;

/// <summary>
/// Review state of a proposal.
/// </summary>
public enum ProposalStatus
{
    /// <summary>Waiting for review.</summary>
    Open,

    /// <summary>Merged by the user in the web UI.</summary>
    Merged,

    /// <summary>Rejected by the user.</summary>
    Rejected,
}
