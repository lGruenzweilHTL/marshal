using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.FollowUps;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Core.Context;
using Marshal.Core.Conversation;
using Marshal.Core.Guard;
using Marshal.Tasks.Notifications;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.FollowUps;

/// <summary>
/// <see cref="IFollowUpReplies"/>: a voice turn is linked by its <c>followupRef</c> (known, unexpired, unconsumed,
/// same satellite) or, without one, as the first turn from the asking satellite within the reply window. Targets:
/// a pending approval is decided by keyword or judge through the approval queue (a neutral reply leaves it pending
/// and is a normal turn); a task waiting for the reply gets it as trigger payload and resumes; a delivered result
/// seeds the turn with its speech and (untrusted) body.
/// </summary>
public sealed class FollowUpReplies(
    IFollowUpStore followUps,
    IPendingActionStore pendingActions,
    IApprovalQueue approvals,
    IConfirmationKeywords keywords,
    IConfirmationJudge judge,
    ITaskStore tasks,
    ITaskScheduler scheduler,
    IOptions<NotificationOptions> options,
    TimeProvider time) : IFollowUpReplies
{
    /// <summary>Top-level key of the trigger payload a spoken reply resumes a task with.</summary>
    public const string PayloadKey = "reply";

    /// <summary>Taint kind of turns seeded with a delivered result (it may summarize third-party content).</summary>
    public const string ResultTaint = "task_result";

    public async Task<FollowUpReply?> HandleAsync(ConversationTurn turn, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        if (turn.Evidence.SatelliteId is not { } satellite || context.SessionId is not { } session)
            return null;

        FollowUpRecord? followUp = turn.FollowupRef is { Length: > 0 } nonce
            ? await followUps.ConsumeAsync(nonce, satellite, session, cancellationToken)
            : turn.InputMode == InputMode.Voice && options.Value.FollowUpReplyWindow > TimeSpan.Zero
                ? await followUps.ConsumeRecentAsync(satellite, options.Value.FollowUpReplyWindow, session, cancellationToken)
                : null;
        if (followUp is null)
            return null;

        if (followUp.PendingActionId is { } actionId)
            return await DecideAsync(actionId, turn.Text, context.Language, cancellationToken);
        if (followUp.TaskId is not { } taskId)
            return null;

        Result<TaskRecord> task = await tasks.GetAsync(taskId, cancellationToken);
        if (task.IsFailure)
            return null;
        if (WaitsForReply(task.Value))
            return await ResumeAsync(task.Value, turn.Text, satellite, context.Language, cancellationToken);
        return task.Value.Result is { } result ? Seed(task.Value, result, context) : null;
    }

    private async Task<FollowUpReply?> DecideAsync(PendingActionId id, string text, string language, CancellationToken cancellationToken)
    {
        PendingActionRecord? action = await pendingActions.GetAsync(id, cancellationToken);
        if (action is null)
            return null;

        bool keyword = keywords.IsConfirmation(text, language);
        JudgeVerdict verdict = keyword ? JudgeVerdict.Positive : await judge.JudgeAsync(action.Summary, text, language, cancellationToken);
        if (verdict is not (JudgeVerdict.Positive or JudgeVerdict.Negative))
            return null;

        bool approve = verdict == JudgeVerdict.Positive;
        Result<PendingActionRecord> decided = await approvals.DecideAsync(id, approve, keyword ? DecidedBy.Keyword : DecidedBy.Judge, cancellationToken);
        string reply = decided.IsSuccess
            ? approve ? FixedReplies.ApprovalGiven(language) : FixedReplies.Cancelled(language)
            : decided.Error.Code == "approval.expired" ? FixedReplies.Expired(language) : FixedReplies.ApprovalClosed(language);
        return new FollowUpReply(reply, []);
    }

    // A task that asked and waits without a run time (not a re-armed schedule or watch, not an approval).
    private static bool WaitsForReply(TaskRecord task) =>
        task.Status == AgentTaskStatus.Waiting && task.NextRunAt is null && task.Trigger.Kind is not (TaskTriggerKind.Cron or TaskTriggerKind.Event);

    private async Task<FollowUpReply?> ResumeAsync(TaskRecord task, string text, string satellite, string language, CancellationToken cancellationToken)
    {
        string payload = new JsonObject { [PayloadKey] = new JsonObject { ["text"] = text, ["satellite"] = satellite } }.ToJsonString();
        Result<TaskRecord> saved = await tasks.UpdateAsync(task with { TriggerPayload = payload, NextRunAt = time.GetUtcNow() }, cancellationToken);
        if (saved.IsFailure)
            return null;

        scheduler.Wake();
        return new FollowUpReply(FixedReplies.ReplyTaken(language), []);
    }

    private static FollowUpReply Seed(TaskRecord task, TaskResult result, MarshalExecutionContext context)
    {
        context.Taint.Mark(ResultTaint, 0);
        string about = $"You delivered this result of task {task.Id}{(task.Name is null ? "" : $" ({task.Name})")} a moment ago; the user's next message refers to it.";
        return new FollowUpReply(null,
        [
            new ChatMessage(ChatRole.System, $"{about}\n{UntrustedContent.Wrap(ResultTaint, result.Body)}"),
            new ChatMessage(ChatRole.Assistant, result.Speech),
        ]);
    }
}
