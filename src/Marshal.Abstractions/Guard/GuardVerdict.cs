using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// The guard's decision on one call.
/// </summary>
public sealed record GuardVerdict
{
    public required GuardOutcome Outcome { get; init; }

    /// <summary>Decision recorded on the run step.</summary>
    public required GuardDecision Decision { get; init; }

    /// <summary>The resolved tool; null when the name is unknown.</summary>
    public RegisteredTool? Tool { get; init; }

    /// <summary>Final tier (only ever raised by the guard).</summary>
    public RiskTier? Tier { get; init; }

    /// <summary>Reasons for confirmation, the spoken one first.</summary>
    public IReadOnlyList<ConfirmationReason> Reasons { get; init; } = [];

    /// <summary>Error given to the model when denied.</summary>
    public Error? Denial { get; init; }
}
