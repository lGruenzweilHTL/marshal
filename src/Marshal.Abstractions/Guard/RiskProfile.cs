using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// The tiers a tool's calls can resolve to, independent of arguments. Used by the visibility filter.
/// </summary>
/// <param name="PossibleTiers">Every tier some call of the tool could get.</param>
public sealed record RiskProfile(IReadOnlySet<RiskTier> PossibleTiers)
{
    /// <summary>True when the tier depends on the arguments.</summary>
    public bool IsArgumentDependent => PossibleTiers.Count > 1;
}
