using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Guard;

/// <summary>
/// What a user turn did to the session's pending action.
/// </summary>
public enum ConfirmationOutcomeKind
{
    /// <summary>No pending action; handle the turn normally.</summary>
    NoPendingAction,

    /// <summary>Confirmed; the frozen action was executed (see <see cref="ConfirmationOutcome.ToolResult"/>).</summary>
    Executed,

    /// <summary>Rejected; reply with a fixed "cancelled" message, no model call.</summary>
    Cancelled,

    /// <summary>Confirmed too late; nothing executed, reply that it expired.</summary>
    Expired,

    /// <summary>Not an answer (or the judge failed); the action is gone and the turn is handled normally.</summary>
    Dropped,
}

/// <summary>
/// Result of <see cref="IConfirmationFlow.HandleUserTurnAsync"/>.
/// </summary>
public sealed record ConfirmationOutcome(
    ConfirmationOutcomeKind Kind,
    PendingActionRecord? Action = null,
    GuardDecision? Decision = null,
    RegisteredTool? Tool = null,
    Result<ToolOutput>? ToolResult = null);
