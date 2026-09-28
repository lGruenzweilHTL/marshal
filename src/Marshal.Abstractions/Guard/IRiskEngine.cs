using System.Text.Json;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// Applies the risk rules from <c>/config/risk.yaml</c> to tools and calls.
/// </summary>
public interface IRiskEngine
{
    /// <summary>Tier of a concrete call. Target rules are evaluated per resolved target; the highest tier wins.</summary>
    Task<RiskAssessment> AssessAsync(RegisteredTool tool, JsonElement arguments, CancellationToken cancellationToken);

    /// <summary>The tiers the tool's calls can resolve to.</summary>
    RiskProfile GetProfile(RegisteredTool tool);
}
