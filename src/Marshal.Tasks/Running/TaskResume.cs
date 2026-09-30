using Marshal.Abstractions;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Running;

/// <summary>
/// Resumes a waiting task: its trigger payload is set, it becomes due now and the scheduler is woken. Tasks that no
/// longer wait are left alone.
/// </summary>
internal static class TaskResume
{
    /// <returns>False when the task no longer waits or could not be saved (after retries on row-version conflicts).</returns>
    public static async Task<bool> ResumeAsync(ITaskStore tasks, ITaskScheduler scheduler, TimeProvider time, TaskId taskId, string payload, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            Result<TaskRecord> task = await tasks.GetAsync(taskId, cancellationToken);
            if (task.IsFailure || task.Value.Status != AgentTaskStatus.Waiting)
                return false;

            Result<TaskRecord> saved = await tasks.UpdateAsync(task.Value with { TriggerPayload = payload, NextRunAt = time.GetUtcNow() }, cancellationToken);
            if (saved.IsSuccess)
            {
                scheduler.Wake();
                return true;
            }
        }

        return false;
    }
}
