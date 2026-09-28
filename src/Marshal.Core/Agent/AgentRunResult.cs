using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;

namespace Marshal.Core.Agent;

/// <summary>
/// Result of one run.
/// </summary>
public sealed record AgentRunResult(
    RunId RunId,
    string Reply,
    bool ContinueConversation,
    RunOutcome Outcome,
    string? OutcomeReason,
    PendingActionRecord? PendingAction);
