namespace Marshal.Abstractions.Tools;

/// <summary>
/// Risk tier of a tool call. Ordered: a higher value is stricter; the guard only ever raises a tier.
/// </summary>
public enum RiskTier
{
    /// <summary>Executes immediately.</summary>
    Safe = 0,

    /// <summary>Executes only after a user action confirms the frozen call.</summary>
    Confirm = 1,

    /// <summary>Never executes.</summary>
    Forbidden = 2,
}
