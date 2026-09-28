namespace Marshal.Core.Guard;

/// <summary>
/// One rule as written in <c>risk.yaml</c>: <c>match</c> holds <c>tool</c> (glob), optional <c>args</c>
/// (argument name → allowed values) and target conditions (attribute → allowed values).
/// </summary>
public sealed class RiskRuleEntry
{
    public Dictionary<string, object?> Match { get; set; } = [];

    public string Tier { get; set; } = "";
}
