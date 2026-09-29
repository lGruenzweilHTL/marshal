using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Scheduling;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Events;

/// <summary>
/// Checks each event against the armed watches (<c>event</c> tasks) and makes the matching ones due with the event as
/// trigger payload. A watch fires once per arming: it is armed again when its run ends. An event with an owner only
/// reaches that owner's watches; instance-wide events (HA, printers) reach every owner's.
/// Watches are read per event (few rows), so there is no cache to keep in step with task changes.
/// </summary>
internal sealed partial class DeclarativeTriggerMatcher(
    ITaskStore store,
    TaskSignals signals,
    IExecutionContextAccessor executionContext,
    TimeProvider time,
    ILogger<DeclarativeTriggerMatcher> logger) : ITriggerMatcher
{
    private const int MaxCachedFilters = 1000;

    private readonly ConcurrentDictionary<string, EventFilter?> _filters = new(StringComparer.Ordinal);

    public async Task MatchAsync(MarshalEvent @event, CancellationToken cancellationToken)
    {
        bool fired = false;
        foreach (TaskWatch watch in await store.GetWatchesAsync(cancellationToken))
        {
            if (@event.OwnerId is { } owner && owner != watch.OwnerId)
                continue;
            if (Filter(watch.Filter) is not { } filter || !filter.Matches(@event))
                continue;

            fired |= await FireAsync(watch, @event, cancellationToken);
        }

        if (fired)
            signals.Wake();
    }

    private async Task<bool> FireAsync(TaskWatch watch, MarshalEvent @event, CancellationToken cancellationToken)
    {
        using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(watch.OwnerId));
        Result<TaskRecord> task = await store.GetAsync(watch.Id, cancellationToken);
        if (task.IsFailure || task.Value.Trigger.Kind != TaskTriggerKind.Event || task.Value.NextRunAt is not null
            || task.Value.Status is not (AgentTaskStatus.Pending or AgentTaskStatus.Waiting))
        {
            return false;
        }

        Result<TaskRecord> saved = await store.UpdateAsync(task.Value with
        {
            Status = AgentTaskStatus.Pending,
            StatusReason = null,
            NextRunAt = time.GetUtcNow(),
            TriggerPayload = EventPayloads.Serialize(@event),
        }, cancellationToken);
        if (saved.IsFailure)
        {
            LogNotFired(watch.Id, saved.Error.Message);
            return false;
        }

        LogFired(watch.Id, @event.Source, @event.Type);
        return true;
    }

    private EventFilter? Filter(string spec)
    {
        if (_filters.TryGetValue(spec, out EventFilter? cached))
            return cached;
        if (_filters.Count >= MaxCachedFilters)
            _filters.Clear();

        Result<EventFilter> parsed = EventFilter.Parse(spec);
        return _filters[spec] = parsed.IsSuccess ? parsed.Value : null;
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Watch {TaskId} fired on {Source} {Type}")]
    private partial void LogFired(TaskId taskId, string source, string type);

    [LoggerMessage(Level = LogLevel.Information, Message = "Watch {TaskId} matched but was not fired: {Reason}")]
    private partial void LogNotFired(TaskId taskId, string reason);
}
