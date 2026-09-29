namespace Marshal.Abstractions.Tasks;

/// <summary>
/// The task scheduler. Call <see cref="Wake"/> after making a task due or changing its run time, so the scheduler
/// re-reads the store instead of sleeping until its previous earliest run time.
/// </summary>
public interface ITaskScheduler
{
    void Wake();

    /// <summary>Stops the task's run in progress, if any (after its status was changed, e.g. to <c>cancelled</c>).</summary>
    void CancelRun(TaskId task);
}
