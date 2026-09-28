using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Agent;

/// <summary>
/// The decision on a pending action made at the start of a turn. When <see cref="Result"/> is set, the frozen
/// action was executed and the model phrases the reply from its result; otherwise the decision is only recorded.
/// </summary>
public sealed record ConfirmationStep(PendingActionRecord Action, RegisteredTool? Tool, GuardDecision Decision, Result<ToolOutput>? Result);
