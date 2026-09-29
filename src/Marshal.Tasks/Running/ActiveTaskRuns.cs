using System.Collections.Concurrent;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Running;

/// <summary>
/// The task runs in progress, so task tools called inside a run can tell the runner how the run should end
/// (e.g. wait until a wakeup time) without touching the task row the scheduler owns while it runs.
/// </summary>
public sealed class ActiveTaskRuns
{
    private readonly ConcurrentDictionary<TaskId, TaskRunState> _runs = new();

    /// <summary>Registers a run of <paramref name="task"/>; dispose the scope when the run ends.</summary>
    public Scope Begin(TaskId task)
    {
        var state = new TaskRunState();
        _runs[task] = state;
        return new Scope(this, task, state);
    }

    /// <summary>The state of the task's run in progress, or null.</summary>
    public TaskRunState? Find(TaskId task) => _runs.GetValueOrDefault(task);

    /// <summary>A registered run.</summary>
    public sealed class Scope(ActiveTaskRuns runs, TaskId task, TaskRunState state) : IDisposable
    {
        public TaskRunState State => state;

        public void Dispose() => runs._runs.TryRemove(new KeyValuePair<TaskId, TaskRunState>(task, state));
    }
}

/// <summary>
/// What task tools requested during a run.
/// </summary>
public sealed class TaskRunState
{
    private readonly Lock _lock = new();

    public bool WaitRequested { get; private set; }

    public DateTimeOffset? WakeAt { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>Ends the run as <c>waiting</c>: until <paramref name="wakeAt"/>, or an external resume when null.</summary>
    public void RequestWait(DateTimeOffset? wakeAt, string? notes)
    {
        lock (_lock)
        {
            WaitRequested = true;
            WakeAt = wakeAt;
            if (notes is not null)
                Notes = notes;
        }
    }
}
