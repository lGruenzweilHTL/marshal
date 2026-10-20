using System.Collections.Concurrent;
using System.Diagnostics;

namespace Marshal.Testing;

/// <summary>
/// Records every stopped activity of the named sources. Listeners are process-wide and tests run in parallel, so
/// read the activities of one trace with <see cref="Trace"/> or <see cref="TraceOf"/>.
/// </summary>
public sealed class ActivityCollector : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<Activity> _stopped = new();

    public ActivityCollector(params string[] sources)
    {
        HashSet<string> names = [.. sources];
        _listener = new ActivityListener
        {
            ShouldListenTo = source => names.Contains(source.Name),
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = _stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(_listener);
    }

    /// <summary>Every stopped activity, of all traces.</summary>
    public IReadOnlyList<Activity> All => [.. _stopped];

    /// <summary>The stopped activities of one trace, in stop order.</summary>
    public IReadOnlyList<Activity> Trace(ActivityTraceId traceId) => [.. _stopped.Where(activity => activity.TraceId == traceId)];

    /// <summary>The stopped activities of the trace whose activity has <paramref name="tag"/> = <paramref name="value"/>.</summary>
    public IReadOnlyList<Activity> TraceOf(string tag, string value)
    {
        Activity first = _stopped.First(activity => activity.GetTagItem(tag) as string == value);
        return Trace(first.TraceId);
    }

    public void Dispose() => _listener.Dispose();
}
