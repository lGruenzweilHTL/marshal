namespace Marshal.Abstractions.Tasks;

/// <summary>
/// The task scheduler. Call <see cref="Wake"/> after making a task due or changing its run time, so the scheduler
/// re-reads the store instead of sleeping until its previous earliest run time.
/// </summary>
public interface ITaskScheduler
{
    void Wake();
}
