namespace Marshal.Abstractions.Guard;

/// <summary>
/// What decided a pending action.
/// </summary>
public enum DecidedBy
{
    /// <summary>Exact confirmation keyword.</summary>
    Keyword,

    /// <summary>The confirmation judge.</summary>
    Judge,

    /// <summary>A web UI approval.</summary>
    Ui,

    /// <summary>A nonce-verified push action.</summary>
    Push,

    /// <summary>The system (expiry, supersede, idle close).</summary>
    System,
}
