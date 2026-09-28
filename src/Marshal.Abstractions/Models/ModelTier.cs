namespace Marshal.Abstractions.Models;

/// <summary>
/// Model tier. Capabilities are flags on each model, not extra tiers.
/// </summary>
public enum ModelTier
{
    /// <summary>Voice turns, the confirmation judge, cheap classification.</summary>
    Fast,

    /// <summary>Normal work.</summary>
    Standard,

    /// <summary>Strong model, only consulted through the advisor tool.</summary>
    Advisor,
}
