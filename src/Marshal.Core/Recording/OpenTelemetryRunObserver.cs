using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Text.Json;
using Marshal.Abstractions.Runs;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Recording;

/// <summary>
/// Emits run metrics from the data the recorder stores. The spans come from the runner (<see cref="RunTracing"/>)
/// and from the model clients while the run executes; with <c>telemetry.captureContent</c> this observer adds the
/// recorded content to them.
/// </summary>
public sealed class OpenTelemetryRunObserver : IRunObserver, IDisposable
{
    public const string MeterName = "Marshal";

    private readonly Meter _meter = new(MeterName);
    private readonly Counter<long> _runs;
    private readonly Counter<long> _tokens;
    private readonly Counter<long> _toolCalls;
    private readonly Histogram<double> _cost;
    private readonly bool _captureContent;

    public OpenTelemetryRunObserver(IOptions<TelemetryOptions>? options = null)
    {
        _captureContent = options?.Value.CaptureContent ?? false;
        _runs = _meter.CreateCounter<long>("marshal.runs", description: "Completed runs by outcome.");
        _tokens = _meter.CreateCounter<long>("marshal.model.tokens", unit: "{token}", description: "Model tokens by direction.");
        _toolCalls = _meter.CreateCounter<long>("marshal.tool.calls", description: "Tool calls by guard decision.");
        _cost = _meter.CreateHistogram<double>("marshal.run.cost", description: "Cost per run.");
    }

    public void RunStarted(RunId runId, RunStart start, DateTimeOffset startedAt)
    {
    }

    public void StepRecorded(RunId runId, int sequence, RunStepRecord step, DateTimeOffset recordedAt)
    {
        if (step.TokensIn is { } input)
            _tokens.Add(input, new KeyValuePair<string, object?>("direction", "input"));
        if (step.TokensOut is { } output)
            _tokens.Add(output, new KeyValuePair<string, object?>("direction", "output"));
        if (step.GuardDecision is { } decision)
            _toolCalls.Add(1, new KeyValuePair<string, object?>("decision", Snake(decision.ToString())));
        if (_captureContent)
            Capture(step);
    }

    public void RunCompleted(RunId runId, RunCompletion completion, decimal cost, DateTimeOffset endedAt)
    {
        _runs.Add(1, new KeyValuePair<string, object?>("outcome", Snake(completion.Outcome.ToString())));
        _cost.Record((double)cost);
    }

    public void Dispose() => _meter.Dispose();

    // The recorder calls observers inside the step's span with the redacted step: content is what the runs view
    // shows (truncated, sensitive results as references, withheld reasoning gone). The system prompt (the context
    // step's content) and model output other than the reply never become attributes.
    private static void Capture(RunStepRecord step)
    {
        if (Activity.Current is not { } activity)
            return;

        switch (step.Kind)
        {
            case RunStepKind.ToolCall when RunTracing.IsTool(activity):
                activity.SetTag("gen_ai.tool.call.arguments", step.Arguments);
                activity.SetTag("gen_ai.tool.call.result", step.Content ?? step.Reference);
                break;
            case RunStepKind.Context when RunTracing.IsRun(activity):
                activity.SetTag("marshal.input", step.Input);
                break;
            case RunStepKind.Reply when RunTracing.IsRun(activity):
                activity.SetTag("marshal.reply", step.Content);
                break;
        }
    }

    private static string Snake(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
