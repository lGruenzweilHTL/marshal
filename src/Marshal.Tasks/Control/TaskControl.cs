using Marshal.Abstractions;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Control;

/// <summary>
/// <see cref="ITaskControl"/> on the task store. Running tasks save often, so a stale row version is retried with a
/// fresh copy a few times before the conflict is reported.
/// </summary>
public sealed class TaskControl(ITaskStore store, ITaskScheduler scheduler) : ITaskControl
{
    private const int UpdateAttempts = 3;

    private static readonly AgentTaskStatus[] _unfinished = [AgentTaskStatus.Pending, AgentTaskStatus.Running, AgentTaskStatus.Waiting];

    public Task<Result<TaskRecord>> SetGoalAsync(TaskId id, string goal, CancellationToken cancellationToken) =>
        UpdateAsync(id, task => task with { Goal = goal }, cancellationToken);

    public async Task<Result<int>> CancelAsync(TaskId id, CancellationToken cancellationToken)
    {
        Result<TaskRecord> root = await store.GetAsync(id, cancellationToken);
        if (root.IsFailure)
            return root.Error;

        int cancelled = 0;
        var pending = new Queue<TaskId>([id]);
        while (pending.TryDequeue(out TaskId next))
        {
            bool unfinished = false;
            Result<TaskRecord> saved = await UpdateAsync(next, task =>
            {
                unfinished = task.Status is AgentTaskStatus.Pending or AgentTaskStatus.Running or AgentTaskStatus.Waiting;
                return unfinished ? task with { Status = AgentTaskStatus.Cancelled, StatusReason = "cancelled", NextRunAt = null } : null;
            }, cancellationToken);
            if (saved.IsFailure)
                return saved.Error;
            if (unfinished)
            {
                scheduler.CancelRun(next);
                cancelled++;
            }

            foreach (TaskRecord child in await store.ListAsync(new TaskFilter(_unfinished, next), cancellationToken))
                pending.Enqueue(child.Id);
        }

        return cancelled;
    }

    // Applies change to a fresh copy; a null change leaves the task as it is.
    private async Task<Result<TaskRecord>> UpdateAsync(TaskId id, Func<TaskRecord, TaskRecord?> change, CancellationToken cancellationToken)
    {
        for (int attempt = 1; ; attempt++)
        {
            Result<TaskRecord> task = await store.GetAsync(id, cancellationToken);
            if (task.IsFailure)
                return task;
            if (change(task.Value) is not { } changed)
                return task;

            Result<TaskRecord> saved = await store.UpdateAsync(changed, cancellationToken);
            if (saved.IsSuccess || saved.Error.Code != TaskErrors.Conflict().Code || attempt == UpdateAttempts)
                return saved;
        }
    }
}
