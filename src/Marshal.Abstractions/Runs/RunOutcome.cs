namespace Marshal.Abstractions.Runs;

/// <summary>
/// How a run ended.
/// </summary>
public enum RunOutcome
{
    /// <summary>Completed normally.</summary>
    Ok,

    /// <summary>Ended by an unexpected error.</summary>
    Error,

    /// <summary>Continued as a background task.</summary>
    Backgrounded,

    /// <summary>Cancelled (e.g. by the latency cap).</summary>
    Cancelled,
}
