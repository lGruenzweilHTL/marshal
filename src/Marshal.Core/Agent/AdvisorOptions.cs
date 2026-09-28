using System.ComponentModel.DataAnnotations;

namespace Marshal.Core.Agent;

/// <summary>
/// Limits of <c>agent.ask_advisor</c>.
/// </summary>
public sealed class AdvisorOptions
{
    /// <summary>Advisor calls per run (templates may override later).</summary>
    [Range(0, 10, ErrorMessage = "agent.advisor.maxCallsPerRun must be between {1} and {2}.")]
    public int MaxCallsPerRun { get; set; } = 2;

    /// <summary>Advisor cost per owner and local day; the tool is hidden once reached.</summary>
    [Range(0, 1000, ErrorMessage = "agent.advisor.dailyCostCap must be between {1} and {2}.")]
    public decimal DailyCostCap { get; set; } = 1m;
}
