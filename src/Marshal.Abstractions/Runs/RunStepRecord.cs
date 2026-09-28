using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;

namespace Marshal.Abstractions.Runs;

/// <summary>
/// One run step. The recorder assigns the sequence number and applies redaction.
/// </summary>
public sealed record RunStepRecord
{
    public required RunStepKind Kind { get; init; }

    /// <summary>Model key (model calls).</summary>
    public string? Model { get; init; }

    public string? Tier { get; init; }

    public long? TokensIn { get; init; }

    public long? TokensOut { get; init; }

    public long? TokensCached { get; init; }

    public int? FirstTokenMs { get; init; }

    /// <summary>Internal tool id (tool calls).</summary>
    public ToolId? Tool { get; init; }

    public RiskTier? RiskTier { get; init; }

    public GuardDecision? GuardDecision { get; init; }

    /// <summary>Confirmation reasons of a pending or escalated call.</summary>
    public IReadOnlyList<ConfirmationReason> Reasons { get; init; } = [];

    /// <summary>Tool arguments (tool calls).</summary>
    public string? Arguments { get; init; }

    /// <summary>User input of the turn (context steps).</summary>
    public string? Input { get; init; }

    /// <summary>Content: system prompt (context), model output, reply text, or tool result. Redacted for sensitive tools.</summary>
    public string? Content { get; init; }

    /// <summary>Reference stored instead of the content for sensitive tools (message id, path).</summary>
    public string? Reference { get; init; }

    /// <summary>True when the tool is flagged sensitive; the content is then never stored.</summary>
    public bool Sensitive { get; init; }

    /// <summary>Reply steps: whether Marshal asked something.</summary>
    public bool? ContinueConversation { get; init; }

    public int DurationMs { get; init; }

    public decimal? Cost { get; init; }

    /// <summary>Error code and message, if the step failed.</summary>
    public string? Error { get; init; }
}
