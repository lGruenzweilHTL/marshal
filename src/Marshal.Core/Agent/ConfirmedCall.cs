using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Agent;

/// <summary>
/// A frozen action that was confirmed and executed before the run; the run records it and lets the model
/// phrase the reply.
/// </summary>
public sealed record ConfirmedCall(PendingActionRecord Action, RegisteredTool? Tool, GuardDecision Decision, Result<ToolOutput> Result);
