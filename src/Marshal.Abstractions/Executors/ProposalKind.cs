namespace Marshal.Abstractions.Executors;

/// <summary>
/// What a proposal changes.
/// </summary>
public enum ProposalKind
{
    /// <summary>A change to the notes repository.</summary>
    Notes,

    /// <summary>A code change from a coding executor.</summary>
    Code,
}
