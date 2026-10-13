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

    /// <summary>Artifacts the turn showed (reply steps only), so a reopened session can render them again.</summary>
    public IReadOnlyList<Artifacts.ArtifactRef>? Artifacts { get; init; }

    /// <summary>True when the tool is flagged sensitive; the content is then never stored.</summary>
    public bool Sensitive { get; init; }

    /// <summary>Model-call steps: the reasoning the model returned, if any.</summary>
    public string? Reasoning { get; init; }

    /// <summary>
    /// Model-call steps: the model reasoned after a redacted tool result in the same run, so the reasoning was not kept.
    /// </summary>
    public bool ReasoningWithheld { get; init; }

    /// <summary>Reply steps: whether Marshal asked something.</summary>
    public bool? ContinueConversation { get; init; }

    public int DurationMs { get; init; }

    public decimal? Cost { get; init; }

    /// <summary>Error code and message, if the step failed.</summary>
    public string? Error { get; init; }

    /// <summary>Delegation depth of the loop that made the step: 0 for the run itself, 1 and more inside subagents.</summary>
    public int Depth { get; init; }

    /// <summary>Context steps: active projects, retrieved memory and similar notes of the context build.</summary>
    public IReadOnlyList<Context.ContextNote> Notes { get; init; } = [];
}
