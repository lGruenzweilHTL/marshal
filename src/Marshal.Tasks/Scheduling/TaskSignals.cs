using System.Collections.Concurrent;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Scheduling;

/// <summary>
/// <see cref="ITaskScheduler"/>: the wake signal and the stop handles of runs in progress. Separate from
/// <see cref="BackgroundTaskScheduler"/>, which consumes them, so task tools and the kill switch can signal the
/// scheduler without depending on it (the scheduler depends on the agent loop, which depends on the task tools).
/// </summary>
public sealed class TaskSignals : ITaskScheduler, IDisposable
{
    private readonly SemaphoreSlim _wake = new(0, 1);
    private readonly ConcurrentDictionary<TaskId, CancellationTokenSource> _runs = new();

    public void Wake()
    {
        try
        {
            if (_wake.CurrentCount == 0)
                _wake.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    public void CancelRun(TaskId task)
    {
        if (_runs.TryGetValue(task, out CancellationTokenSource? run))
            run.Cancel();
    }

    /// <summary>Completes on the next <see cref="Wake"/> (or at once when one is pending).</summary>
    internal Task WaitAsync(CancellationToken cancellationToken) => _wake.WaitAsync(cancellationToken);

    internal void RunStarted(TaskId task, CancellationTokenSource run) => _runs[task] = run;

    internal void RunEnded(TaskId task, CancellationTokenSource run) => _runs.TryRemove(new KeyValuePair<TaskId, CancellationTokenSource>(task, run));

    public void Dispose() => _wake.Dispose();
}
