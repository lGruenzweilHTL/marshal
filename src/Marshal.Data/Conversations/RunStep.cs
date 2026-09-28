using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;

namespace Marshal.Data.Conversations;

/// <summary>
/// One ordered step of a run. Carries the owner of its run.
/// </summary>
public sealed class RunStep : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required RunId RunId { get; init; }

    public required int Sequence { get; init; }

    public required RunStepKind Kind { get; init; }

    public string? Model { get; init; }

    public string? Tier { get; init; }

    public int? TokensIn { get; init; }

    public int? TokensOut { get; init; }

    public int? TokensCached { get; init; }

    /// <summary>Time to first token of a model call.</summary>
    public int? FirstTokenMs { get; init; }

    public string? Tool { get; init; }

    public RiskTier? RiskTier { get; init; }

    public GuardDecision? GuardDecision { get; init; }

    /// <summary>JSON payload, redacted for sensitive tools.</summary>
    public string? Payload { get; set; }

    public int DurationMs { get; init; }

    public decimal? Cost { get; init; }

    public string? Error { get; init; }
}
