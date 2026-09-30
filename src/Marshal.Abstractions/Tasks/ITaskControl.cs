namespace Marshal.Abstractions.Tasks;

/// <summary>
/// User actions on the current owner's tasks (task tools and the tasks view).
/// </summary>
public interface ITaskControl
{
    /// <summary>Replaces the goal; the next run uses it. A running task keeps it when its run ends.</summary>
    Task<Result<TaskRecord>> SetGoalAsync(TaskId id, string goal, CancellationToken cancellationToken);

    /// <summary>Cancels the task and its unfinished descendants, stopping their runs; returns how many were cancelled.</summary>
    Task<Result<int>> CancelAsync(TaskId id, CancellationToken cancellationToken);
}
