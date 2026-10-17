using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;

namespace Marshal.Api.Approvals;

/// <summary>A background approval in the review queue.</summary>
/// <param name="Id">Pending action id.</param>
/// <param name="TaskId">The task that waits for the decision; null for an approval of a run without a task (an AI task).</param>
/// <param name="RunId">The run that asked; with a taint source's first step it links to the tool result that tainted the run.</param>
/// <param name="Tool">Frozen tool id.</param>
/// <param name="Arguments">Frozen arguments; approving executes exactly these.</param>
/// <param name="Summary">What the action does.</param>
/// <param name="Reasons">Why it needs approval, verbatim, the main one first.</param>
/// <param name="Taint">Untrusted sources the run had read, with the step that brought each in.</param>
/// <param name="CreatedAt">When it was queued.</param>
/// <param name="ExpiresAt">When it expires (counts as rejected).</param>
public sealed record ApprovalItem(
    string Id,
    string? TaskId,
    string? RunId,
    string Tool,
    JsonElement Arguments,
    string Summary,
    IReadOnlyList<ConfirmationReason> Reasons,
    IReadOnlyList<TaintSource> Taint,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt)
{
    internal static ApprovalItem From(PendingActionRecord action)
    {
        using var arguments = JsonDocument.Parse(action.ArgumentsJson);
        return new ApprovalItem(
            action.Id.ToString(),
            action.TaskId?.ToString(),
            action.RunId?.ToString(),
            action.Tool.ToString(),
            arguments.RootElement.Clone(),
            action.Summary,
            action.Reasons,
            action.Taint,
            action.CreatedAt,
            action.ExpiresAt);
    }
}

/// <summary>The outcome of a decision.</summary>
/// <param name="Id">Pending action id.</param>
/// <param name="Status">New status: <c>approved</c> or <c>rejected</c>.</param>
public sealed record ApprovalDecision(string Id, PendingActionStatus Status);
