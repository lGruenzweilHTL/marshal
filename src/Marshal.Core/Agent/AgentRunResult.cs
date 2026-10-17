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
    PendingActionRecord? PendingAction)
{
    /// <summary>The reply shaped into <see cref="AgentRunRequest.Structure"/>, when the request asked for one.</summary>
    public System.Text.Json.JsonElement? Data { get; init; }
}
