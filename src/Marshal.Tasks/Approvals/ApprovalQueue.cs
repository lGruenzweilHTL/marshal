#pragma warning disable CA1711 // "Queue" is the domain term (the review queue), not a collection type.

using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Guard;
using Marshal.Data;
using Marshal.Tasks.Running;
using Marshal.Tasks.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Approvals;

/// <summary>
/// <see cref="IApprovalQueue"/>: a background run's confirm-tier call is frozen for <see cref="TaskOptions.ApprovalTtl"/>
/// and sent to the owner as an actionable push (reasons verbatim). A decision from the review queue or a
/// nonce-verified push action executes exactly the frozen action as the task's owner and resumes the waiting task
/// with a payload saying what was decided and what came of it. Expiry counts as rejection.
/// </summary>
public sealed partial class ApprovalQueue(
    IPendingActionStore store,
    ITaskStore tasks,
    INotificationSender notifications,
    IToolRegistry registry,
    IRiskEngine riskEngine,
    IExecutionContextAccessor executionContext,
    IDbContextFactory<MarshalDbContext> contexts,
    ITaskScheduler scheduler,
    IOptions<TaskOptions> options,
    TimeProvider time,
    ILogger<ApprovalQueue> logger) : IApprovalQueue
{
    /// <summary>Prefix of the push action that approves (followed by the nonce).</summary>
    public const string ApprovePrefix = "MARSHAL_APPROVE_";

    /// <summary>Prefix of the push action that rejects (followed by the nonce).</summary>
    public const string RejectPrefix = "MARSHAL_REJECT_";

    /// <summary>Top-level key of the trigger payload a decision resumes a task with.</summary>
    public const string PayloadKey = "approval";

    /// <summary>Key of the payload's taint kind, present when the executed action returned untrusted content.</summary>
    public const string TaintKey = "taint";

    public async Task<PendingActionRecord> EnqueueAsync(FreezeRequest request, CancellationToken cancellationToken)
    {
        PendingActionRecord frozen = await store.FreezeAsync(request with { ExpiresAt = time.GetUtcNow() + options.Value.ApprovalTtl }, cancellationToken);
        bool german = frozen.Language == "de";
        Result<Guid> sent = await notifications.SendAsync(new NotificationRequest
        {
            Owner = frozen.OwnerId,
            Source = $"task:{frozen.TaskId}",
            Priority = NotificationPriority.Normal,
            Channel = DeliveryChannel.Push,
            Text = $"{frozen.Summary}. {string.Join(" ", frozen.Reasons.Select(reason => reason.Sentence))}",
            TaskId = frozen.TaskId,
            PendingActionId = frozen.Id,
            Link = $"review/{frozen.Id}",
            Tag = $"approval-{frozen.Id}",
            Actions =
            [
                new NotificationAction(ApprovePrefix + frozen.Nonce, german ? "Freigeben" : "Approve"),
                new NotificationAction(RejectPrefix + frozen.Nonce, german ? "Ablehnen" : "Reject"),
            ],
        }, cancellationToken);
        if (sent.IsFailure)
            LogNotifyFailed(frozen.Id, sent.Error.Message);
        return frozen;
    }

    public Task<IReadOnlyList<PendingActionRecord>> ListPendingAsync(CancellationToken cancellationToken) =>
        store.ListTaskApprovalsAsync(cancellationToken);

    public async Task<Result<PendingActionRecord>> DecideAsync(PendingActionId id, bool approve, DecidedBy decidedBy, CancellationToken cancellationToken)
    {
        PendingActionRecord? action = await store.GetAsync(id, cancellationToken);
        if (action?.TaskId is not { } taskId)
            return ApprovalErrors.NotFound();
        if (action.Status != PendingActionStatus.Pending)
            return ApprovalErrors.AlreadyDecided();

        Result<TaskRecord> task = await tasks.GetAsync(taskId, cancellationToken);
        if (task.IsSuccess && task.Value.Status == AgentTaskStatus.Running)
            return ApprovalErrors.TaskBusy();
        if (task.IsFailure || task.Value.Status != AgentTaskStatus.Waiting)
        {
            await store.DecideAsync(id, PendingActionStatus.Dropped, DecidedBy.System, cancellationToken);
            return ApprovalErrors.TaskEnded();
        }

        if (time.GetUtcNow() >= action.ExpiresAt)
        {
            await ExpireAsync(action, cancellationToken);
            return ApprovalErrors.Expired();
        }

        Result decided = await store.DecideAsync(id, approve ? PendingActionStatus.Approved : PendingActionStatus.Rejected, decidedBy, cancellationToken);
        if (decided.IsFailure)
            return decided.Error.Type == ErrorType.Conflict ? ApprovalErrors.AlreadyDecided() : ApprovalErrors.NotFound();

        (JsonObject payload, string? taint) = approve
            ? await ExecuteAsync(action, task.Value, cancellationToken)
            : (Payload(action, "rejected"), null);
        await ResumeAsync(taskId, payload, taint, cancellationToken);
        LogDecided(id, approve ? "approved" : "rejected", decidedBy);
        return action with { Status = approve ? PendingActionStatus.Approved : PendingActionStatus.Rejected };
    }

    /// <summary>
    /// Handles a <c>mobile_app_notification_action</c> identifier. Only an approve / reject action with the nonce of a
    /// pending background approval is accepted, decided as that approval's owner; anything else is logged and ignored.
    /// </summary>
    /// <returns>True when a decision was made.</returns>
    public async Task<bool> HandlePushActionAsync(string action, CancellationToken cancellationToken)
    {
        bool approve = action.StartsWith(ApprovePrefix, StringComparison.Ordinal);
        if (!approve && !action.StartsWith(RejectPrefix, StringComparison.Ordinal))
            return false;
        string nonce = action[(approve ? ApprovePrefix : RejectPrefix).Length..];

        Owned? owned = null;
        if (nonce.Length > 0)
        {
            await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
            owned = await db.PendingActions.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.Nonce == nonce && row.TaskId != null)
                .Select(row => new Owned(row.Id, row.OwnerId))
                .SingleOrDefaultAsync(cancellationToken);
        }

        if (owned is null)
        {
            LogUnknownNonce();
            return false;
        }

        using (executionContext.Use(BackgroundTaskScheduler.SystemContext(owned.OwnerId)))
        {
            Result<PendingActionRecord> decided = await DecideAsync(owned.Id, approve, DecidedBy.Push, cancellationToken);
            if (decided.IsFailure)
                LogPushRefused(owned.Id, decided.Error.Code);
            return decided.IsSuccess;
        }
    }

    /// <summary>Expires the overdue background approvals of every owner and resumes their tasks with a rejection.</summary>
    public async Task ExpireDueAsync(CancellationToken cancellationToken)
    {
        List<Owned> overdue;
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            DateTimeOffset now = time.GetUtcNow();
            overdue = await db.PendingActions.IgnoreQueryFilters().AsNoTracking()
                .Where(row => row.TaskId != null && row.Status == PendingActionStatus.Pending && row.ExpiresAt <= now)
                .Select(row => new Owned(row.Id, row.OwnerId))
                .ToListAsync(cancellationToken);
        }

        foreach (Owned approval in overdue)
        {
            using (executionContext.Use(BackgroundTaskScheduler.SystemContext(approval.OwnerId)))
            {
                PendingActionRecord? action = await store.GetAsync(approval.Id, cancellationToken);
                if (action?.TaskId is not { } taskId)
                    continue;

                // The asking run is still finishing; the next pass expires it.
                Result<TaskRecord> task = await tasks.GetAsync(taskId, cancellationToken);
                if (task.IsSuccess && task.Value.Status == AgentTaskStatus.Running)
                    continue;

                await ExpireAsync(action, cancellationToken);
            }
        }
    }

    private async Task ExpireAsync(PendingActionRecord action, CancellationToken cancellationToken)
    {
        Result expired = await store.DecideAsync(action.Id, PendingActionStatus.Expired, DecidedBy.System, cancellationToken);
        if (expired.IsFailure)
            return;

        JsonObject payload = Payload(action, "rejected");
        payload["reason"] = "expired";
        await ResumeAsync(action.TaskId!.Value, payload, null, cancellationToken);
        LogDecided(action.Id, "expired", DecidedBy.System);
    }

    // Exactly the frozen tool and arguments, as the task's owner in a background context. Untrusted output taints the
    // resumed run with the tool's taint kind.
    private async Task<(JsonObject Payload, string? Taint)> ExecuteAsync(PendingActionRecord action, TaskRecord task, CancellationToken cancellationToken)
    {
        var context = new MarshalExecutionContext
        {
            OwnerId = action.OwnerId,
            Source = SessionSource.Task,
            InputMode = InputMode.Text,
            Language = action.Language,
            SatelliteId = action.SatelliteId ?? task.OriginSatelliteId,
            IsInteractive = false,
            Template = task.Template ?? "default",
            TaskId = task.Id,
            ProjectId = task.ProjectId,
            RunId = action.RunId,
        };

        RegisteredTool? tool;
        Result<ToolOutput> result;
        using (executionContext.Use(context))
            (tool, result) = await FrozenActions.ExecuteAsync(registry, riskEngine, action, context, cancellationToken);

        JsonObject payload = Payload(action, "approved");
        if (result.IsFailure)
        {
            payload["error"] = result.Error.Message;
            return (payload, null);
        }

        payload["result"] = result.Value.Content;
        return (payload, tool is { Descriptor.UntrustedOutput: true } ? tool.Descriptor.TaintKind ?? tool.Id.Namespace : null);
    }

    private static JsonObject Payload(PendingActionRecord action, string decision) => new()
    {
        ["id"] = action.Id.ToString(),
        ["tool"] = action.Tool.ToString(),
        ["summary"] = action.Summary,
        ["decision"] = decision,
    };

    private async Task ResumeAsync(TaskId taskId, JsonObject approval, string? taint, CancellationToken cancellationToken)
    {
        var payload = new JsonObject { [PayloadKey] = approval };
        if (taint is not null)
            payload[TaintKey] = taint;

        if (!await TaskResume.ResumeAsync(tasks, scheduler, time, taskId, payload.ToJsonString(), cancellationToken))
            LogResumeFailed(taskId);
    }

    private sealed record Owned(PendingActionId Id, OwnerId OwnerId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Approval {PendingActionId} was queued, but its push failed: {Reason}")]
    private partial void LogNotifyFailed(PendingActionId pendingActionId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Approval {PendingActionId}: {Decision} by {DecidedBy}")]
    private partial void LogDecided(PendingActionId pendingActionId, string decision, DecidedBy decidedBy);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Push approval action with an unknown nonce ignored")]
    private partial void LogUnknownNonce();

    [LoggerMessage(Level = LogLevel.Information, Message = "Push decision for approval {PendingActionId} refused: {Code}")]
    private partial void LogPushRefused(PendingActionId pendingActionId, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {TaskId} was not resumed with its approval decision (it no longer waits or could not be saved)")]
    private partial void LogResumeFailed(TaskId taskId);
}
