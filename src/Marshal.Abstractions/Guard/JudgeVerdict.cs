namespace Marshal.Abstractions.Guard;

/// <summary>
/// Verdict of the confirmation judge.
/// </summary>
public enum JudgeVerdict
{
    /// <summary>The reply confirms the action.</summary>
    Positive,

    /// <summary>Not a clear answer (e.g. topic change); the action is dropped and the message handled normally.</summary>
    Neutral,

    /// <summary>The reply rejects the action.</summary>
    Negative,

    /// <summary>Timeout, error or unparsable output; the action is dropped (fail closed).</summary>
    Failed,
}
