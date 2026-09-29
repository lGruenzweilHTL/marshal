using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Scheduling;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Deliveries;

/// <summary>
/// <see cref="IDeliveryDispatcher"/>: sends each delivery through <see cref="INotificationSender"/>. A delivery with
/// <c>when</c> conditions that do not hold yet waits in the task's delivery list (<c>pendingSince</c>, <c>expiresAt</c>)
/// until a state change makes them hold or its deadline passes. A newer result replaces a waiting one. Silent
/// deliveries send nothing (the result is in the web UI).
/// </summary>
internal sealed partial class DeliveryDispatcher(
    ITaskStore store,
    IConditionEvaluator conditions,
    INotificationSender sender,
    IExecutionContextAccessor executionContext,
    IUserProfileProvider profiles,
    TimeProvider time,
    ILogger<DeliveryDispatcher> logger) : IDeliveryDispatcher
{
    private const int SaveAttempts = 3;

    public Task DispatchAsync(TaskId task, CancellationToken cancellationToken) =>
        UpdateDeliveriesAsync(task, async (record, delivery, now, zone) =>
        {
            DateTimeOffset? expiresAt = delivery.Expires is { } expires ? DeliveryRules.ExpiresAt(expires, now, zone) : null;
            TaskDelivery fresh = delivery with { PendingSince = null, ExpiresAt = null };
            if (delivery.When is { Count: > 0 } && !await HoldAsync(record, delivery, cancellationToken))
                return fresh with { PendingSince = now, ExpiresAt = expiresAt };

            await SendAsync(record, fresh, fallback: false, cancellationToken);
            return fresh;
        }, cancellationToken);

    public async Task ReviewPendingAsync(string? changedEntity, CancellationToken cancellationToken)
    {
        foreach (OwnedTask pending in await store.GetPendingDeliveriesAsync(cancellationToken))
        {
            using (executionContext.Use(BackgroundTaskScheduler.SystemContext(pending.OwnerId)))
            {
                await UpdateDeliveriesAsync(pending.Id, async (record, delivery, now, _) =>
                {
                    if (delivery.PendingSince is null)
                        return delivery;
                    if (delivery.ExpiresAt is { } expiresAt && expiresAt <= now)
                    {
                        await SendAsync(record, delivery, fallback: true, cancellationToken);
                        return delivery with { PendingSince = null, ExpiresAt = null };
                    }

                    if (changedEntity is not null && !DependsOn(delivery, changedEntity))
                        return delivery;
                    if (!await HoldAsync(record, delivery, cancellationToken))
                        return delivery;

                    await SendAsync(record, delivery, fallback: false, cancellationToken);
                    return delivery with { PendingSince = null, ExpiresAt = null };
                }, cancellationToken);
            }
        }
    }

    // Sending happens once per delivery; a conflicting save is retried with a fresh copy, where deliveries already
    // handled are no longer pending.
    private async Task UpdateDeliveriesAsync(TaskId id, Func<TaskRecord, TaskDelivery, DateTimeOffset, TimeZoneInfo, Task<TaskDelivery>> update, CancellationToken cancellationToken)
    {
        Result<TaskRecord> loaded = await store.GetAsync(id, cancellationToken);
        if (loaded.IsFailure || loaded.Value.Result is null)
            return;

        TaskRecord task = loaded.Value;
        TimeZoneInfo zone = await profiles.TimeZoneAsync(task.OwnerId, cancellationToken);
        DateTimeOffset now = time.GetUtcNow();
        var deliveries = new List<TaskDelivery>();
        foreach (TaskDelivery delivery in task.Deliveries)
            deliveries.Add(await update(task, delivery, now, zone));
        if (deliveries.SequenceEqual(task.Deliveries, DeliveryComparer.Instance))
            return;

        for (int attempt = 0; attempt < SaveAttempts; attempt++)
        {
            Result<TaskRecord> saved = await store.UpdateAsync(task with { Deliveries = deliveries }, cancellationToken);
            if (saved.IsSuccess || saved.Error != TaskErrors.Conflict())
                return;

            Result<TaskRecord> fresh = await store.GetAsync(id, cancellationToken);
            if (fresh.IsFailure)
                return;
            task = fresh.Value;
        }

        LogSaveGaveUp(id);
    }

    private async Task<bool> HoldAsync(TaskRecord task, TaskDelivery delivery, CancellationToken cancellationToken)
    {
        var parsed = new List<StateCondition>();
        foreach (string condition in delivery.When ?? [])
        {
            Result<StateCondition> result = StateCondition.Parse(condition);
            if (result.IsFailure)
                return false;
            parsed.Add(result.Value);
        }

        return await conditions.AllHoldAsync(parsed, task.OwnerId, cancellationToken);
    }

    private static bool DependsOn(TaskDelivery delivery, string entity) =>
        (delivery.When ?? []).Select(StateCondition.Parse).Any(condition => condition.IsSuccess
            && (condition.Value.Entity == entity || condition.Value.Entity == StateCondition.Person && entity.StartsWith("person.", StringComparison.Ordinal)));

    private async Task SendAsync(TaskRecord task, TaskDelivery delivery, bool fallback, CancellationToken cancellationToken)
    {
        DeliveryChannel? channel = fallback ? delivery.Fallback : delivery.Channel;
        if (channel is null or DeliveryChannel.Silent || task.Result is not { } result)
            return;

        Result<Guid> sent = await sender.SendAsync(new NotificationRequest
        {
            Owner = task.OwnerId,
            Source = $"task:{task.Id}",
            Priority = delivery.Priority,
            Text = string.IsNullOrWhiteSpace(result.Speech) ? result.Body : result.Speech,
            Channel = channel,
            Target = fallback ? null : delivery.Target,
            FollowUp = !fallback && delivery.FollowUp,
            TaskId = task.Id,
            Link = $"tasks/{task.Id}",
            OverrideQuietHoursUntil = task.OverrideQuietHoursUntil,
            IsFallback = fallback,
        }, cancellationToken);
        if (sent.IsFailure)
            LogSendFailed(task.Id, sent.Error.Message);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivery of task {TaskId} failed: {Reason}")]
    private partial void LogSendFailed(TaskId taskId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Delivery state of task {TaskId} could not be saved after repeated conflicts")]
    private partial void LogSaveGaveUp(TaskId taskId);

    // Records compare their lists by reference; deliveries are compared by value.
    private sealed class DeliveryComparer : IEqualityComparer<TaskDelivery>
    {
        public static DeliveryComparer Instance { get; } = new();

        public bool Equals(TaskDelivery? x, TaskDelivery? y) =>
            x is not null && y is not null && x with { When = null } == y with { When = null } && (x.When ?? []).SequenceEqual(y.When ?? []);

        public int GetHashCode(TaskDelivery obj) => (obj with { When = null }).GetHashCode();
    }
}
