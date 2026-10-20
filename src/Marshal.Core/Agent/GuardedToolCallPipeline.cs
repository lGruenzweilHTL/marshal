using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Core.Context;
using Marshal.Core.Guard;
using Marshal.Core.Recording;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Agent;

/// <summary>
/// <see cref="IToolCallPipeline"/> over <see cref="IGuard"/>: a denied call is recorded with its denial, a call that
/// needs confirmation is frozen (into the session, or into the approval queue for non-interactive runs), an allowed
/// call executes. Untrusted results taint the run before they reach the caller.
/// </summary>
public sealed partial class GuardedToolCallPipeline(
    IGuard guard,
    IPendingActionStore pendingActions,
    IRunRecorder recorder,
    IOptions<ConfirmationOptions> confirmationOptions,
    TimeProvider timeProvider,
    ILogger<GuardedToolCallPipeline> logger,
    IApprovalQueue? approvals = null,
    IToolRedaction? redaction = null) : IToolCallPipeline
{
    private readonly IToolRedaction _redaction = redaction ?? new SensitiveToolRedaction();

    public async Task<ToolCallOutcome> CallAsync(ToolCallRequest request, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = request.Context;
        GuardVerdict verdict = await guard.EvaluateAsync(new GuardRequest(request.ModelName, request.Arguments, context, request.Offered), cancellationToken);
        if (verdict.Outcome == GuardOutcome.RequireConfirmation && request.ConfirmationDenial is { } refused)
            verdict = verdict with { Outcome = GuardOutcome.Deny, Decision = GuardDecision.Denied, Denial = refused };

        string toolName = verdict.Tool?.Id.ToString() ?? request.ModelName;
        LogToolDecision(logger, toolName, verdict.Decision);
        if (request.OnDecided is not null)
            await request.OnDecided(verdict, cancellationToken);

        switch (verdict.Outcome)
        {
            case GuardOutcome.Deny:
                await RecordAsync(context, ToolCallSteps.Step(verdict.Tool, request.ModelName, request.Arguments, verdict.Tier, GuardDecision.Denied, [], null, verdict.Denial, 0, request.Notes?.Invoke()), cancellationToken);
                return new ToolCallOutcome(ToolCallSteps.ErrorText(verdict.Denial!)) { Verdict = verdict };

            case GuardOutcome.RequireConfirmation:
                return await FreezeAsync(request, verdict, cancellationToken);

            default:
                long started = timeProvider.GetTimestamp();
                Result<ToolOutput> result = await InvokeAsync(verdict.Tool!, request.Arguments, context, cancellationToken);
                int duration = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds;
                return await CompleteAsync(verdict.Tool!, request.ModelName, request.Arguments, verdict.Tier, verdict.Decision, result, duration, context, request.Notes, cancellationToken) with { Verdict = verdict };
        }
    }

    public Task<ToolCallOutcome> CompleteConfirmedAsync(ConfirmedToolCall call, CancellationToken cancellationToken) =>
        CompleteAsync(call.Tool, call.ModelName, call.Arguments, RiskTier.Confirm, call.Decision, call.Result, 0, call.Context, null, cancellationToken);

    private async Task<ToolCallOutcome> FreezeAsync(ToolCallRequest request, GuardVerdict verdict, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = request.Context;
        RegisteredTool tool = verdict.Tool!;
        var freeze = new FreezeRequest(
            context.SessionId,
            context.SatelliteId,
            tool.Id,
            request.Arguments.GetRawText(),
            tool.Tool.Summarize(request.Arguments, context),
            verdict.Reasons,
            context.Language,
            timeProvider.GetUtcNow() + confirmationOptions.Value.Ttl,
            context.TaskId,
            context.RunId,
            context.Taint.Sources);

        // A non-interactive run never holds a pending action in its session; its confirmations go to the approval queue.
        bool queues = approvals is not null && !context.IsInteractive;
        PendingActionRecord frozen = queues
            ? await approvals!.EnqueueAsync(freeze, cancellationToken)
            : await pendingActions.FreezeAsync(freeze, cancellationToken);
        await RecordAsync(context, ToolCallSteps.Step(tool, request.ModelName, request.Arguments, verdict.Tier, verdict.Decision, verdict.Reasons, null, null, 0, request.Notes?.Invoke()), cancellationToken);

        // Nobody in a run without a task can confirm: the action waits in the queue and the run goes on.
        return queues && context.TaskId is null
            ? new ToolCallOutcome(QueuedText(frozen)) { Verdict = verdict, Frozen = frozen, Queued = true }
            : new ToolCallOutcome(PendingText(frozen)) { Verdict = verdict, Frozen = frozen };
    }

    private async Task<ToolCallOutcome> CompleteAsync(
        RegisteredTool tool, string modelName, JsonElement arguments, RiskTier? tier, GuardDecision decision, Result<ToolOutput> result, int duration,
        MarshalExecutionContext context, Func<IReadOnlyList<ContextNote>>? notes, CancellationToken cancellationToken)
    {
        int step = await RecordAsync(context, ToolCallSteps.Step(
            tool, modelName, arguments, tier, decision, [], result.IsSuccess ? result.Value : null, result.IsFailure ? result.Error : null, duration, notes?.Invoke()), cancellationToken);
        if (result.IsFailure)
            return new ToolCallOutcome(ToolCallSteps.ErrorText(result.Error)) { Result = result, DurationMs = duration };

        bool redacted = _redaction.Redacts(tool.Id, tool.Descriptor.Sensitive);
        string? kind = tool.Descriptor.UntrustedOutput ? tool.Descriptor.TaintKind ?? tool.Id.Namespace : result.Value.TaintKind;
        if (kind is null)
            return new ToolCallOutcome(result.Value.Content) { Result = result, DurationMs = duration, Redacted = redacted };

        // Taint is set before the result enters the context and never cleared within the run.
        context.Taint.Mark(kind, step);
        return new ToolCallOutcome(UntrustedContent.Wrap(kind, result.Value.Content)) { Result = result, DurationMs = duration, Redacted = redacted };
    }

    private async Task<Result<ToolOutput>> InvokeAsync(RegisteredTool tool, JsonElement arguments, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        try
        {
            return await tool.Tool.InvokeAsync(new ToolInvocation(arguments, context), cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A crashing tool must not end the turn; the model gets an error like for any expected failure.
            LogToolCrashed(logger, exception, tool.Id.ToString());
            return new Error("tool.failed", "The tool failed unexpectedly.", ErrorType.Failure);
        }
    }

    private Task<int> RecordAsync(MarshalExecutionContext context, RunStepRecord step, CancellationToken cancellationToken) =>
        recorder.RecordStepAsync(context.RunId ?? throw new InvalidOperationException("A tool call needs a run."), step with { Depth = context.Depth }, cancellationToken);

    private static string QueuedText(PendingActionRecord action) =>
        $"Waiting for approval in the review queue. The action was NOT done yet and will run only if the user approves it later: {action.Summary}. Say so in your answer; do not ask for confirmation.";

    private static string PendingText(PendingActionRecord action)
    {
        string reasons = string.Join(" ", action.Reasons.Select(reason => reason.Sentence));
        return $"Confirmation pending. The action was NOT done yet. Ask the user to confirm: {action.Summary}. Include this reason in your question: {reasons}";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool call {Tool}: {Decision}")]
    private static partial void LogToolDecision(ILogger logger, string tool, GuardDecision decision);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool {Tool} threw an exception")]
    private static partial void LogToolCrashed(ILogger logger, Exception exception, string tool);
}

/// <summary>Tool call step records and the error text the model gets.</summary>
public static class ToolCallSteps
{
    /// <summary>The step of a tool call; also tags the current <c>execute_tool</c> span.</summary>
    public static RunStepRecord Step(
        RegisteredTool? tool, string modelName, JsonElement arguments, RiskTier? tier, GuardDecision decision,
        IReadOnlyList<ConfirmationReason> reasons, ToolOutput? output, Error? error, int duration, IReadOnlyList<ContextNote>? notes = null)
    {
        ToolId? id = tool?.Id ?? GuessId(modelName);
        RunTracing.TagTool(id, tier, decision, error?.Code);
        return new RunStepRecord
        {
            Notes = output?.Notes is { Count: > 0 } extra ? [.. notes ?? [], .. extra] : notes ?? [],
            Kind = RunStepKind.ToolCall,
            Tool = id,
            RiskTier = tier,
            GuardDecision = decision,
            Reasons = reasons,
            Arguments = arguments.GetRawText(),
            Content = output?.Content,
            Reference = output?.Reference,
            Sensitive = tool?.Descriptor.Sensitive ?? false,
            DurationMs = duration,
            Error = error is null ? null : $"{error.Code}: {error.Message}",
        };
    }

    public static string ErrorText(Error error) =>
        JsonSerializer.Serialize(new { error = new { code = error.Code, message = error.Message } });

    /// <summary>Unknown names map to the id they would have (the first '_' separates the namespace).</summary>
    public static ToolId? GuessId(string modelName)
    {
        int separator = modelName.IndexOf('_', StringComparison.Ordinal);
        Result<ToolId> parsed = separator > 0 ? ToolId.Parse($"{modelName[..separator]}.{modelName[(separator + 1)..]}") : ToolId.Parse(modelName);
        return parsed.IsSuccess ? parsed.Value : null;
    }
}
