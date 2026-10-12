using Marshal.Abstractions;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Store;

/// <summary>
/// Publishes <c>task.changed</c> on the <c>tasks</c> topic after every successful create and update, and
/// <c>conversation.result</c> on <c>conversations</c> when an update gives a task handed off from a session a new result.
/// </summary>
internal sealed class LiveTaskStore(EfTaskStore inner, ILiveEventPublisher publisher) : ITaskStore
{
    public async Task<Result<TaskRecord>> CreateAsync(NewTask task, CancellationToken cancellationToken) =>
        Published(await inner.CreateAsync(task, cancellationToken));

    public Task<Result<TaskRecord>> GetAsync(TaskId id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);

    public Task<Result<TaskRecord>> FindAsync(string reference, CancellationToken cancellationToken) => inner.FindAsync(reference, cancellationToken);

    public Task<IReadOnlyList<TaskRecord>> ListAsync(TaskFilter filter, CancellationToken cancellationToken) => inner.ListAsync(filter, cancellationToken);

    public async Task<Result<TaskRecord>> UpdateAsync(TaskRecord task, CancellationToken cancellationToken)
    {
        // Only a changed result time is news; the previous value has to be read before the update overwrites it.
        DateTimeOffset? previousResultAt = task is { OriginSessionId: not null, ResultAt: not null }
            && await inner.GetAsync(task.Id, cancellationToken) is { IsSuccess: true } stored
            ? stored.Value.ResultAt
            : null;
        Result<TaskRecord> updated = Published(await inner.UpdateAsync(task, cancellationToken));
        if (updated.IsSuccess && updated.Value is { OriginSessionId: { } session, ResultAt: { } resultAt, Result: { } result } && resultAt != previousResultAt)
        {
            publisher.Publish(new LiveEvent(ConversationLiveEvents.Conversations, ConversationLiveEvents.Result, Guid.CreateVersion7().ToString(), updated.Value.OwnerId,
                new ConversationResultPayload(session, updated.Value.Id, resultAt, result.Body)));
        }

        return updated;
    }

    public Task<IReadOnlyList<OwnedTask>> GetDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken) => inner.GetDueAsync(now, limit, cancellationToken);

    public Task<DateTimeOffset?> GetNextRunAtAsync(CancellationToken cancellationToken) => inner.GetNextRunAtAsync(cancellationToken);

    public Task<IReadOnlyList<TaskWatch>> GetWatchesAsync(CancellationToken cancellationToken) => inner.GetWatchesAsync(cancellationToken);

    public Task<IReadOnlyList<OwnedTask>> GetPendingDeliveriesAsync(CancellationToken cancellationToken) => inner.GetPendingDeliveriesAsync(cancellationToken);

    public Task<IReadOnlyList<OwnedTask>> GetRunningAsync(CancellationToken cancellationToken) => inner.GetRunningAsync(cancellationToken);

    private Result<TaskRecord> Published(Result<TaskRecord> result)
    {
        if (result.IsSuccess)
        {
            TaskRecord task = result.Value;
            publisher.Publish(new LiveEvent(TaskLiveEvents.Tasks, "task.changed", Guid.CreateVersion7().ToString(), task.OwnerId,
                new TaskChangedPayload(task.Id, task.Name, task.Status, task.StatusReason, task.NextRunAt, task.ResultAt)));
        }

        return result;
    }
}
