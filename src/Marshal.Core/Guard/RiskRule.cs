using Marshal.Abstractions.Tools;

namespace Marshal.Core.Guard;

/// <summary>
/// A compiled risk rule.
/// </summary>
/// <param name="ToolPattern">Glob on the internal tool id.</param>
/// <param name="ArgumentConditions">Top-level argument name → allowed values (case-insensitive).</param>
/// <param name="TargetConditions">Target attribute → allowed values (case-insensitive), evaluated per resolved target.</param>
/// <param name="Tier">Tier when the rule matches.</param>
public sealed record RiskRule(
    string ToolPattern,
    IReadOnlyDictionary<string, IReadOnlyList<string>> ArgumentConditions,
    IReadOnlyDictionary<string, IReadOnlyList<string>> TargetConditions,
    RiskTier Tier)
{
    public bool IsUnconditional => ArgumentConditions.Count == 0 && TargetConditions.Count == 0;
}
