using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Marshal.Abstractions.Runs;

namespace Marshal.Core.Recording;

/// <summary>
/// Emits one trace per run (a span per step) and run metrics from the data the recorder stores. Spans carry ids
/// and metadata only, never content.
/// </summary>
public sealed class OpenTelemetryRunObserver : IRunObserver, IDisposable
{
    public const string SourceName = "Marshal.Runs";
    public const string MeterName = "Marshal";

    private readonly ActivitySource _source = new(SourceName);
    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _runs;
    private readonly Counter<long> _tokens;
    private readonly Counter<long> _toolCalls;
    private readonly Histogram<double> _cost;
    private readonly ConcurrentDictionary<RunId, RunTrace> _open = new();

    public OpenTelemetryRunObserver()
    {
        _runs = _meter.CreateCounter<long>("marshal.runs", description: "Completed runs by outcome.");
        _tokens = _meter.CreateCounter<long>("marshal.model.tokens", unit: "{token}", description: "Model tokens by direction.");
        _toolCalls = _meter.CreateCounter<long>("marshal.tool.calls", description: "Tool calls by guard decision.");
        _cost = _meter.CreateHistogram<double>("marshal.run.cost", description: "Cost per run.");
    }

    public void RunStarted(RunId runId, RunStart start, DateTimeOffset startedAt) =>
        _open[runId] = new RunTrace(start, startedAt);

    public void StepRecorded(RunId runId, int sequence, RunStepRecord step, DateTimeOffset recordedAt)
    {
        if (step.TokensIn is { } input)
            _tokens.Add(input, new KeyValuePair<string, object?>("direction", "input"));
        if (step.TokensOut is { } output)
            _tokens.Add(output, new KeyValuePair<string, object?>("direction", "output"));
        if (step.GuardDecision is { } decision)
            _toolCalls.Add(1, new KeyValuePair<string, object?>("decision", Snake(decision.ToString())));

        if (_open.TryGetValue(runId, out RunTrace? trace))
        {
            lock (trace.Steps)
            {
                trace.Steps.Add((sequence, step, recordedAt));
            }
        }
    }

    public void RunCompleted(RunId runId, RunCompletion completion, decimal cost, DateTimeOffset endedAt)
    {
        string outcome = Snake(completion.Outcome.ToString());
        _runs.Add(1, new KeyValuePair<string, object?>("outcome", outcome));
        _cost.Record((double)cost);
        if (!_open.TryRemove(runId, out RunTrace? trace))
            return;

        using Activity? root = _source.StartActivity("marshal.run", ActivityKind.Internal, default(ActivityContext), startTime: trace.StartedAt);
        if (root is null)
            return;

        root.SetTag("marshal.run_id", runId.ToString());
        root.SetTag("marshal.source", Snake(trace.Start.Context.Source.ToString()));
        root.SetTag("marshal.input_mode", Snake(trace.Start.Context.InputMode.ToString()));
        root.SetTag("marshal.trigger", trace.Start.Trigger);
        root.SetTag("marshal.outcome", outcome);
        root.SetTag("marshal.outcome_reason", completion.Reason);
        root.SetTag("marshal.tainted", completion.TaintSources.Count > 0);
        foreach ((int sequence, RunStepRecord step, DateTimeOffset recordedAt) in trace.Steps.OrderBy(entry => entry.Sequence))
        {
            using Activity? span = _source.StartActivity($"marshal.{Snake(step.Kind.ToString())}", ActivityKind.Internal, root.Context, startTime: recordedAt - TimeSpan.FromMilliseconds(step.DurationMs));
            if (span is null)
                continue;

            span.SetTag("marshal.sequence", sequence);
            span.SetTag("marshal.model", step.Model);
            span.SetTag("marshal.tier", step.Tier);
            span.SetTag("marshal.tool", step.Tool?.ToString());
            span.SetTag("marshal.guard_decision", step.GuardDecision is { } decision ? Snake(decision.ToString()) : null);
            span.SetTag("marshal.tokens_in", step.TokensIn);
            span.SetTag("marshal.tokens_out", step.TokensOut);
            span.SetEndTime(recordedAt.UtcDateTime);
        }

        root.SetEndTime(endedAt.UtcDateTime);
    }

    public void Dispose()
    {
        _source.Dispose();
        _meter.Dispose();
    }

    private static string Snake(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);

    private sealed record RunTrace(RunStart Start, DateTimeOffset StartedAt)
    {
        public List<(int Sequence, RunStepRecord Step, DateTimeOffset RecordedAt)> Steps { get; } = [];
    }
}
