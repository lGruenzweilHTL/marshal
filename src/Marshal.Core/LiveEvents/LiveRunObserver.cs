using System.Collections.Concurrent;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Runs;

namespace Marshal.Core.LiveEvents;

/// <summary>
/// Publishes <c>run.started</c> and <c>run.completed</c> on the <c>runs</c> topic to the run's owner.
/// </summary>
public sealed class LiveRunObserver(ILiveEventPublisher publisher) : IRunObserver
{
    private readonly ConcurrentDictionary<RunId, OwnerId> _owners = new();

    public void RunStarted(RunId runId, RunStart start, DateTimeOffset startedAt)
    {
        _owners[runId] = start.Context.OwnerId;
        Publish("run.started", start.Context.OwnerId, new RunStartedPayload(runId, start.Context.Source, start.Trigger, startedAt));
    }

    public void StepRecorded(RunId runId, int sequence, RunStepRecord step, DateTimeOffset recordedAt)
    {
    }

    public void RunCompleted(RunId runId, RunCompletion completion, decimal cost, DateTimeOffset endedAt)
    {
        if (_owners.TryRemove(runId, out OwnerId owner))
            Publish("run.completed", owner, new RunCompletedPayload(runId, completion.Outcome, cost, endedAt));
    }

    private void Publish(string type, OwnerId owner, object payload) =>
        publisher.Publish(new LiveEvent(LiveTopics.Runs, type, Guid.CreateVersion7().ToString(), owner, payload));
}

/// <summary>Payload of <c>run.started</c>.</summary>
public sealed record RunStartedPayload(RunId RunId, SessionSource Source, string Trigger, DateTimeOffset StartedAt);

/// <summary>Payload of <c>run.completed</c>.</summary>
public sealed record RunCompletedPayload(RunId RunId, RunOutcome Outcome, decimal Cost, DateTimeOffset EndedAt);
