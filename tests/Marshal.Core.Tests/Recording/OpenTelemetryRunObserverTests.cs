using System.Diagnostics;
using System.Diagnostics.Metrics;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Core.Recording;
using Marshal.Testing;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Recording;

public sealed class OpenTelemetryRunObserverTests
{
    [Fact]
    public void Run_EmitsTokenToolCallRunAndCostMetrics()
    {
        var measurements = new List<(string Instrument, double Value, string Tag)>();
        using var listener = new MeterListener
        {
            InstrumentPublished = (instrument, meterListener) =>
            {
                if (instrument.Meter.Name == OpenTelemetryRunObserver.MeterName)
                    meterListener.EnableMeasurementEvents(instrument);
            },
        };
        listener.SetMeasurementEventCallback<long>((instrument, value, tags, _) => Add(instrument, value, tags));
        listener.SetMeasurementEventCallback<double>((instrument, value, tags, _) => Add(instrument, value, tags));
        listener.Start();
        using var observer = new OpenTelemetryRunObserver();
        var runId = RunId.New();

        observer.RunStarted(runId, new RunStart(TestExecutionContext.Voice(), "turn"), DateTimeOffset.UnixEpoch);
        observer.StepRecorded(runId, 0, new RunStepRecord { Kind = RunStepKind.ModelCall, TokensIn = 10, TokensOut = 5 }, DateTimeOffset.UnixEpoch);
        observer.StepRecorded(runId, 1, new RunStepRecord { Kind = RunStepKind.ToolCall, Tool = new ToolId("ha", "HassTurnOn"), GuardDecision = GuardDecision.Safe }, DateTimeOffset.UnixEpoch);
        observer.RunCompleted(runId, new RunCompletion(RunOutcome.Ok, null, []), 0.25m, DateTimeOffset.UnixEpoch);

        lock (measurements)
        {
            Assert.Contains(("marshal.model.tokens", 10d, "direction=input"), measurements);
            Assert.Contains(("marshal.model.tokens", 5d, "direction=output"), measurements);
            Assert.Contains(("marshal.tool.calls", 1d, "decision=safe"), measurements);
            Assert.Contains(("marshal.runs", 1d, "outcome=ok"), measurements);
            Assert.Contains(("marshal.run.cost", 0.25d, ""), measurements);
        }

        void Add(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string tag = string.Join(",", tags.ToArray().Select(pair => $"{pair.Key}={pair.Value}"));
            lock (measurements)
                measurements.Add((instrument.Name, value, tag));
        }
    }

    [Fact]
    public void CaptureOn_ToolSpanCarriesRecordedArgumentsAndResult()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        using OpenTelemetryRunObserver observer = Capturing();

        using (RunTracing.StartTool("ha_HassTurnOn", "call-1"))
            observer.StepRecorded(RunId.New(), 1, new RunStepRecord { Kind = RunStepKind.ToolCall, Arguments = """{"name":"kitchen"}""", Content = "turned on" }, DateTimeOffset.UnixEpoch);

        Activity span = Assert.Single(collector.All, activity => activity.GetTagItem("gen_ai.tool.call.id") as string == "call-1");
        Assert.Equal("""{"name":"kitchen"}""", span.GetTagItem("gen_ai.tool.call.arguments"));
        Assert.Equal("turned on", span.GetTagItem("gen_ai.tool.call.result"));
    }

    [Fact]
    public void CaptureOn_SensitiveResult_SpanCarriesTheReference()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        using OpenTelemetryRunObserver observer = Capturing();

        // The recorder hands observers the redacted step: a sensitive result has its content removed.
        using (RunTracing.StartTool("mail_read", "call-2"))
            observer.StepRecorded(RunId.New(), 1, new RunStepRecord { Kind = RunStepKind.ToolCall, Arguments = "{}", Reference = "mail:inbox/42", Sensitive = true }, DateTimeOffset.UnixEpoch);

        Activity span = Assert.Single(collector.All, activity => activity.GetTagItem("gen_ai.tool.call.id") as string == "call-2");
        Assert.Equal("mail:inbox/42", span.GetTagItem("gen_ai.tool.call.result"));
    }

    [Fact]
    public void CaptureOn_RootCarriesInputAndReplyNeverPromptOrReasoning()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        using OpenTelemetryRunObserver observer = Capturing();
        var runId = RunId.New();

        using (Activity? root = RunTracing.StartRun(TestExecutionContext.Text(), "default", "turn"))
        {
            root?.SetTag("marshal.run_id", runId.ToString());
            observer.StepRecorded(runId, 0, new RunStepRecord { Kind = RunStepKind.Context, Content = "You are Marshal. SYSTEM PROMPT", Input = "how warm is it?" }, DateTimeOffset.UnixEpoch);
            observer.StepRecorded(runId, 1, new RunStepRecord { Kind = RunStepKind.ModelCall, Content = "model text", Reasoning = "thinking about it" }, DateTimeOffset.UnixEpoch);
            observer.StepRecorded(runId, 2, new RunStepRecord { Kind = RunStepKind.Reply, Content = "It is 21 degrees." }, DateTimeOffset.UnixEpoch);
        }

        Activity span = Assert.Single(collector.TraceOf("marshal.run_id", runId.ToString()));
        Assert.Equal("how warm is it?", span.GetTagItem("marshal.input"));
        Assert.Equal("It is 21 degrees.", span.GetTagItem("marshal.reply"));
        List<string> values = [.. span.TagObjects.Select(tag => tag.Value?.ToString() ?? "")];
        Assert.DoesNotContain(values, value => value.Contains("SYSTEM PROMPT", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains("thinking", StringComparison.Ordinal));
        Assert.DoesNotContain(values, value => value.Contains("model text", StringComparison.Ordinal));
    }

    [Fact]
    public void CaptureOff_NoContentOnSpans()
    {
        using var collector = new ActivityCollector(RunTracing.SourceName);
        using var observer = new OpenTelemetryRunObserver();

        using (RunTracing.StartTool("ha_HassTurnOn", "call-3"))
            observer.StepRecorded(RunId.New(), 1, new RunStepRecord { Kind = RunStepKind.ToolCall, Arguments = "{}", Content = "turned on" }, DateTimeOffset.UnixEpoch);

        Activity span = Assert.Single(collector.All, activity => activity.GetTagItem("gen_ai.tool.call.id") as string == "call-3");
        Assert.Null(span.GetTagItem("gen_ai.tool.call.arguments"));
        Assert.Null(span.GetTagItem("gen_ai.tool.call.result"));
    }

    [Fact]
    public void CaptureOn_ForeignCurrentActivity_Untouched()
    {
        using var collector = new ActivityCollector("test.foreign");
        using var foreign = new ActivitySource("test.foreign");
        using OpenTelemetryRunObserver observer = Capturing();

        using (Activity? activity = foreign.StartActivity("execute_tool"))
        {
            activity?.SetTag("test.id", "foreign-1");
            observer.StepRecorded(RunId.New(), 2, new RunStepRecord { Kind = RunStepKind.Reply, Content = "It is 21 degrees." }, DateTimeOffset.UnixEpoch);
        }

        Activity span = Assert.Single(collector.TraceOf("test.id", "foreign-1"));
        Assert.Null(span.GetTagItem("marshal.reply"));
    }

    private static OpenTelemetryRunObserver Capturing() => new(Options.Create(new TelemetryOptions { CaptureContent = true }));
}
