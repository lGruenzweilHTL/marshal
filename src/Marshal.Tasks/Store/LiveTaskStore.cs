using Marshal.Abstractions;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Store;

/// <summary>
/// Publishes <c>task.changed</c> on the <c>tasks</c> topic after every successful create and update.
/// </summary>
internal sealed class LiveTaskStore(EfTaskStore inner, ILiveEventPublisher publisher) : ITaskStore
{
    public async Task<Result<TaskRecord>> CreateAsync(NewTask task, CancellationToken cancellationToken) =>
        Published(await inner.CreateAsync(task, cancellationToken));

    public Task<Result<TaskRecord>> GetAsync(TaskId id, CancellationToken cancellationToken) => inner.GetAsync(id, cancellationToken);

    public Task<Result<TaskRecord>> FindAsync(string reference, CancellationToken cancellationToken) => inner.FindAsync(reference, cancellationToken);

    public Task<IReadOnlyList<TaskRecord>> ListAsync(TaskFilter filter, CancellationToken cancellationToken) => inner.ListAsync(filter, cancellationToken);

    public async Task<Result<TaskRecord>> UpdateAsync(TaskRecord task, CancellationToken cancellationToken) =>
        Published(await inner.UpdateAsync(task, cancellationToken));

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
