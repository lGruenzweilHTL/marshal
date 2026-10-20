using System.Diagnostics;
using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Recording;

/// <summary>
/// The activity source of runs: an <c>invoke_agent</c> span per run and an <c>execute_tool</c> span per tool call,
/// named and tagged after the OpenTelemetry GenAI semantic conventions (<see cref="GenAiConventions"/>). The spans
/// are current while the run executes, so model calls and outgoing requests parent on them. They carry ids and
/// metadata only; content is added by <see cref="OpenTelemetryRunObserver"/> when capture is on.
/// </summary>
public static class RunTracing
{
    public const string SourceName = "Marshal.Runs";

    public const string InvokeAgent = "invoke_agent";

    public const string ExecuteTool = "execute_tool";

    public static ActivitySource Source { get; } = new(SourceName);

    /// <summary>Starts the run's root span; null without a listener.</summary>
    public static Activity? StartRun(MarshalExecutionContext context, string agentName, string trigger)
    {
        Activity? activity = Source.StartActivity(InvokeAgent, ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.DisplayName = $"{InvokeAgent} {agentName}";
        activity.SetTag("gen_ai.operation.name", InvokeAgent);
        activity.SetTag("gen_ai.agent.name", agentName);
        activity.SetTag("gen_ai.conversation.id", context.SessionId?.ToString());
        activity.SetTag("marshal.source", Snake(context.Source.ToString()));
        activity.SetTag("marshal.input_mode", Snake(context.InputMode.ToString()));
        activity.SetTag("marshal.trigger", trigger);
        return activity;
    }

    public static void CompleteRun(Activity? activity, RunOutcome outcome, string? reason, bool tainted)
    {
        if (activity is null)
            return;

        activity.SetTag("marshal.outcome", Snake(outcome.ToString()));
        activity.SetTag("marshal.outcome_reason", reason);
        activity.SetTag("marshal.tainted", tainted);
        if (outcome == RunOutcome.Error)
            activity.SetStatus(ActivityStatusCode.Error, reason);
    }

    /// <summary>Starts a tool call's span under the current run span; null without a listener.</summary>
    public static Activity? StartTool(string modelName, string callId)
    {
        Activity? activity = Source.StartActivity(ExecuteTool, ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.DisplayName = $"{ExecuteTool} {modelName}";
        activity.SetTag("gen_ai.operation.name", ExecuteTool);
        activity.SetTag("gen_ai.tool.name", modelName);
        activity.SetTag("gen_ai.tool.call.id", callId);
        activity.SetTag("gen_ai.tool.type", "function");
        return activity;
    }

    /// <summary>Tags the current tool span with the guard's view of the call. A denial is a decision, not an error.</summary>
    public static void TagTool(ToolId? tool, RiskTier? tier, GuardDecision decision, string? errorCode)
    {
        if (Activity.Current is not { } activity || !IsTool(activity))
            return;

        activity.SetTag("marshal.tool", tool?.ToString());
        activity.SetTag("marshal.risk_tier", tier is { } value ? Snake(value.ToString()) : null);
        activity.SetTag("marshal.guard_decision", Snake(decision.ToString()));
        if (errorCode is not null && decision != GuardDecision.Denied)
        {
            activity.SetStatus(ActivityStatusCode.Error);
            activity.SetTag("error.type", errorCode);
        }
    }

    public static void Fail(Activity? activity, Exception exception)
    {
        if (activity is null)
            return;

        activity.SetStatus(ActivityStatusCode.Error, exception.Message);
        activity.SetTag("error.type", exception.GetType().FullName);
    }

    public static bool IsRun(Activity activity) => activity.Source == Source && activity.OperationName == InvokeAgent;

    public static bool IsTool(Activity activity) => activity.Source == Source && activity.OperationName == ExecuteTool;

    private static string Snake(string name) => JsonNamingPolicy.SnakeCaseLower.ConvertName(name);
}
