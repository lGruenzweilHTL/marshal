using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Runs;
using Marshal.Core.LiveEvents;
using Marshal.Testing;

namespace Marshal.Core.Tests.LiveEvents;

public sealed class LiveRunObserverTests
{
    [Fact]
    public void RunStartedAndCompleted_ArePublishedToTheRunsOwnerOnTheRunsTopic()
    {
        var published = new List<LiveEvent>();
        var observer = new LiveRunObserver(new CapturingPublisher(published));
        var run = RunId.New();

        observer.RunStarted(run, new RunStart(TestExecutionContext.Voice(TestExecutionContext.OwnerB), "turn"), DateTimeOffset.UnixEpoch);
        observer.StepRecorded(run, 0, new RunStepRecord { Kind = RunStepKind.Context }, DateTimeOffset.UnixEpoch);
        observer.RunCompleted(run, new RunCompletion(RunOutcome.Ok, null, []), 0.25m, DateTimeOffset.UnixEpoch.AddSeconds(2));

        Assert.Equal(["run.started", "run.completed"], published.Select(entry => entry.Type));
        Assert.All(published, entry => Assert.Equal((LiveTopics.Runs, TestExecutionContext.OwnerB), (entry.Topic, entry.Owner)));
        Assert.Equal(published.Count, published.Select(entry => entry.Id).Distinct().Count());
    }

    [Fact]
    public void RunCompletedWithoutStart_IsIgnored()
    {
        var published = new List<LiveEvent>();
        var observer = new LiveRunObserver(new CapturingPublisher(published));

        observer.RunCompleted(RunId.New(), new RunCompletion(RunOutcome.Ok, null, []), 0m, DateTimeOffset.UnixEpoch);

        Assert.Empty(published);
    }

    private sealed class CapturingPublisher(List<LiveEvent> published) : ILiveEventPublisher
    {
        public void Publish(LiveEvent liveEvent) => published.Add(liveEvent);
    }
}
