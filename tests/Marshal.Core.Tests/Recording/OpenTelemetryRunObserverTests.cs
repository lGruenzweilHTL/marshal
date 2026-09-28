using System.Diagnostics;
using Marshal.Abstractions.Runs;
using Marshal.Core.Recording;
using Marshal.Testing;

namespace Marshal.Core.Tests.Recording;

public sealed class OpenTelemetryRunObserverTests
{
    [Fact]
    public void RunCompleted_ExportsOneTraceWithSpanPerStep()
    {
        var stopped = new List<Activity>();
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == OpenTelemetryRunObserver.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Add,
        };
        ActivitySource.AddActivityListener(listener);
        using var observer = new OpenTelemetryRunObserver();
        var runId = RunId.New();
        var start = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);

        observer.RunStarted(runId, new RunStart(TestExecutionContext.Voice(), "turn"), start);
        observer.StepRecorded(runId, 0, new RunStepRecord { Kind = RunStepKind.Context }, start.AddMilliseconds(5));
        observer.StepRecorded(runId, 1, new RunStepRecord { Kind = RunStepKind.ModelCall, Model = "fast", DurationMs = 300, TokensIn = 10, TokensOut = 5 }, start.AddMilliseconds(310));
        observer.StepRecorded(runId, 2, new RunStepRecord { Kind = RunStepKind.ToolCall, Tool = new Abstractions.Tools.ToolId("ha", "HassTurnOn"), GuardDecision = Abstractions.Guard.GuardDecision.Safe, DurationMs = 50 }, start.AddMilliseconds(365));
        observer.StepRecorded(runId, 3, new RunStepRecord { Kind = RunStepKind.Reply }, start.AddMilliseconds(370));
        observer.RunCompleted(runId, new RunCompletion(RunOutcome.Ok, null, []), 0.1m, start.AddMilliseconds(400));

        Activity root = Assert.Single(stopped, activity => activity.OperationName == "marshal.run");
        List<Activity> children = [.. stopped.Where(activity => activity.ParentSpanId == root.SpanId)];
        Assert.Equal(4, children.Count);
        Assert.All(stopped, activity => Assert.Equal(root.TraceId, activity.TraceId));
        Assert.Equal(start, new DateTimeOffset(root.StartTimeUtc, TimeSpan.Zero));
        Assert.Equal(TimeSpan.FromMilliseconds(400), root.Duration);
        Assert.Equal("ok", root.GetTagItem("marshal.outcome"));
        Activity tool = Assert.Single(children, child => child.OperationName == "marshal.tool_call");
        Assert.Equal("ha.HassTurnOn", tool.GetTagItem("marshal.tool"));
        Assert.Equal(TimeSpan.FromMilliseconds(50), tool.Duration);
    }

    [Fact]
    public void WithoutListener_DoesNothing()
    {
        using var observer = new OpenTelemetryRunObserver();
        var runId = RunId.New();

        observer.RunStarted(runId, new RunStart(TestExecutionContext.Text(), "turn"), DateTimeOffset.UnixEpoch);
        observer.RunCompleted(runId, new RunCompletion(RunOutcome.Ok, null, []), 0m, DateTimeOffset.UnixEpoch);
        observer.RunCompleted(RunId.New(), new RunCompletion(RunOutcome.Ok, null, []), 0m, DateTimeOffset.UnixEpoch);
    }
}
