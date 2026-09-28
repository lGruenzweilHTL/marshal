using System.Diagnostics;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Core.Context;
using Marshal.Core.Guard;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Agent;

/// <summary>
/// Marshal's own thin agent loop: build context → call the model with the visible tools → pass every call
/// through the guard → execute, freeze or deny → repeat until a reply or the step limit. Tool invocation is
/// never automatic, so every call goes through the guard.
/// </summary>
public sealed partial class AgentRunner(
    IModelRouter router,
    IToolRegistry registry,
    ITemplateCatalog templates,
    IContextBuilder contextBuilder,
    IToolVisibilityFilter visibility,
    IGuard guard,
    IPendingActionStore pendingActions,
    IRunRecorder recorder,
    ICostCalculator costs,
    IExecutionContextAccessor executionContext,
    IOptions<AgentOptions> agentOptions,
    IOptions<ConfirmationOptions> confirmationOptions,
    TimeProvider timeProvider,
    ILogger<AgentRunner> logger) : IAgentRunner
{
    private static readonly JsonSerializerOptions _argumentJson = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        // A subagent records its steps in its caller's run, marked with its depth.
        bool nested = request.Context is { IsSubagent: true, RunId: not null };
        RunId runId;
        if (nested)
        {
            runId = request.Context.RunId!.Value;
        }
        else
        {
            using (executionContext.Use(request.Context))
                runId = await recorder.StartRunAsync(new RunStart(request.Context, request.Trigger), cancellationToken);
        }

        // The run keeps the tools of its start; sources added or removed meanwhile apply to the next run.
        ToolSnapshot tools = request.Context.Tools ?? await registry.RefreshAsync(cancellationToken);
        MarshalExecutionContext context = request.Context with { RunId = runId, Tools = tools };
        using IDisposable scope = executionContext.Use(context);
        LogRunStarted(logger, runId.Value, request.Trigger);

        using CancellationTokenSource latencyCap = request.LatencyCap is { } cap ? new CancellationTokenSource(cap, timeProvider) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, latencyCap.Token);
        LoopResult result;
        try
        {
            result = await LoopAsync(request, context, linked.Token);
        }
        catch (OperationCanceledException) when (latencyCap.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            if (request.Events is not null)
                await request.Events.WriteAsync(new StatusEvent(FixedReplies.LatencyStatus(context.Language)), cancellationToken);

            result = new LoopResult(FixedReplies.LatencyCap(context.Language), null, RunOutcome.Cancelled, "latency_cap");
        }

        bool continueConversation = result.Pending is not null;
        await RecordAsync(new RunStepRecord { Kind = RunStepKind.Reply, Content = result.Reply, ContinueConversation = continueConversation }, cancellationToken);
        if (!nested)
            await recorder.CompleteRunAsync(runId, new RunCompletion(result.Outcome, result.Reason, context.Taint.Sources), cancellationToken);
        LogRunCompleted(logger, runId.Value, result.Outcome, result.Reason);
        return new AgentRunResult(runId, result.Reply, continueConversation, result.Outcome, result.Reason, result.Pending);
    }

    private async Task<LoopResult> LoopAsync(AgentRunRequest request, MarshalExecutionContext context, CancellationToken ct)
    {
        string systemPrompt = await contextBuilder.BuildSystemPromptAsync(new ContextRequest(context, request.UserText), ct);
        await RecordAsync(new RunStepRecord
        {
            Kind = RunStepKind.Context,
            Content = systemPrompt,
            Input = request.UserText,
        }, ct);

        List<ChatMessage> messages = [new(ChatRole.System, systemPrompt), .. request.History];
        if (request.UserText is not null)
            messages.Add(new ChatMessage(ChatRole.User, request.UserText));
        if (request.Confirmation is not null)
            await AppendConfirmationAsync(request.Confirmation, context, messages, ct);

        if (request.FixedReply is not null)
        {
            if (request.Events is not null)
                await request.Events.WriteAsync(new DeltaEvent(request.FixedReply), ct);

            return new LoopResult(request.FixedReply, null, RunOutcome.Ok, null);
        }

        AgentTemplate template = templates.For(context);
        ModelTier tier = template.ModelTier ?? (context.InputMode == InputMode.Voice ? ModelTier.Fast : ModelTier.Standard);
        int maxSteps = template.MaxSteps ?? agentOptions.Value.MaxSteps;
        Result<RoutedModel> routed = router.Route(new ModelRequest(tier, Tools: true));
        if (routed.IsFailure)
            return new LoopResult(FixedReplies.ModelUnavailable(context.Language), null, RunOutcome.Error, "model_unavailable");

        RoutedModel model = routed.Value;
        PendingActionRecord? pending = null;
        int rounds = 0;
        while (true)
        {
            IReadOnlyList<RegisteredTool> visible = pending is null ? await visibility.GetVisibleToolsAsync(context, ct) : [];
            ChatResponse response = await CallModelAsync(model, messages, visible, request.Events, ct);
            messages.AddRange(response.Messages);

            List<FunctionCallContent> calls = [.. response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>()];
            if (calls.Count == 0)
                return new LoopResult(response.Text, pending, RunOutcome.Ok, null);

            if (rounds == maxSteps)
                return new LoopResult(FixedReplies.StepLimit(context.Language), pending, RunOutcome.Error, "step_limit");

            rounds++;
            HashSet<string> offered = [.. visible.Select(tool => tool.ModelName)];
            var results = new List<AIContent>(calls.Count);
            foreach (FunctionCallContent call in calls)
            {
                (string text, PendingActionRecord? frozen) = await HandleCallAsync(call, context, offered, pending, ct);
                pending ??= frozen;
                results.Add(new FunctionResultContent(call.CallId, text));
            }

            messages.Add(new ChatMessage(ChatRole.Tool, results));
        }
    }

    private async Task<ChatResponse> CallModelAsync(RoutedModel model, List<ChatMessage> messages, IReadOnlyList<RegisteredTool> tools, System.Threading.Channels.ChannelWriter<TurnEvent>? events, CancellationToken ct)
    {
        var options = new ChatOptions
        {
            Tools = tools.Count == 0
                ? null
                : [.. tools.Select(tool => AIFunctionFactory.CreateDeclaration(tool.ModelName, tool.Descriptor.Description, tool.Descriptor.ParametersSchema))],
        };

        long started = timeProvider.GetTimestamp();
        int? firstToken = null;
        var updates = new List<ChatResponseUpdate>();
        await foreach (ChatResponseUpdate update in model.Client.GetStreamingResponseAsync(messages, options, ct))
        {
            updates.Add(update);
            string text = update.Text;
            if (firstToken is null && (text.Length > 0 || update.Contents.Any(content => content is FunctionCallContent)))
                firstToken = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds;
            if (text.Length > 0 && events is not null)
                await events.WriteAsync(new DeltaEvent(text), ct);
        }

        var response = updates.ToChatResponse();
        await RecordAsync(new RunStepRecord
        {
            Kind = RunStepKind.ModelCall,
            Model = model.Key,
            Tier = model.Tier.ToString().ToLowerInvariant(),
            TokensIn = response.Usage?.InputTokenCount,
            TokensOut = response.Usage?.OutputTokenCount,
            TokensCached = response.Usage?.CachedInputTokenCount,
            FirstTokenMs = firstToken,
            DurationMs = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds,
            Cost = costs.Calculate(model.Key, response.Usage),
            Content = response.Text,
        }, ct);
        return response;
    }

    private async Task<(string Text, PendingActionRecord? Frozen)> HandleCallAsync(
        FunctionCallContent call, MarshalExecutionContext context, IReadOnlySet<string> offered, PendingActionRecord? pending, CancellationToken ct)
    {
        JsonElement arguments = JsonSerializer.SerializeToElement(call.Arguments ?? new Dictionary<string, object?>(), _argumentJson);
        if (pending is not null)
        {
            var notNow = new Error("confirmation.already_pending", "Not executed: another action is waiting for confirmation. Ask about that one first.", ErrorType.Conflict);
            await RecordToolStepAsync(null, call.Name, arguments, null, GuardDecision.Denied, [], null, notNow, 0, ct);
            return (ErrorText(notNow), null);
        }

        GuardVerdict verdict = await guard.EvaluateAsync(new GuardRequest(call.Name, arguments, context, offered), ct);
        LogToolDecision(logger, verdict.Tool?.Id.ToString() ?? call.Name, verdict.Decision);
        switch (verdict.Outcome)
        {
            case GuardOutcome.Deny:
                await RecordToolStepAsync(verdict.Tool, call.Name, arguments, verdict.Tier, GuardDecision.Denied, [], null, verdict.Denial, 0, ct);
                return (ErrorText(verdict.Denial!), null);

            case GuardOutcome.RequireConfirmation:
                RegisteredTool confirmTool = verdict.Tool!;
                string summary = confirmTool.Tool.Summarize(arguments, context.Language);
                PendingActionRecord frozen = await pendingActions.FreezeAsync(new FreezeRequest(
                    context.SessionId,
                    context.SatelliteId,
                    confirmTool.Id,
                    arguments.GetRawText(),
                    summary,
                    verdict.Reasons,
                    context.Language,
                    timeProvider.GetUtcNow() + confirmationOptions.Value.Ttl), ct);
                await RecordToolStepAsync(confirmTool, call.Name, arguments, verdict.Tier, verdict.Decision, verdict.Reasons, null, null, 0, ct);
                return (PendingText(frozen), frozen);

            default:
                return (await ExecuteAsync(verdict.Tool!, call.Name, arguments, verdict, context, ct), null);
        }
    }

    private async Task<string> ExecuteAsync(RegisteredTool tool, string modelName, JsonElement arguments, GuardVerdict verdict, MarshalExecutionContext context, CancellationToken ct)
    {
        long started = timeProvider.GetTimestamp();
        Result<ToolOutput> result = await InvokeAsync(tool, arguments, context, ct);
        int duration = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        return await CompleteToolCallAsync(tool, modelName, arguments, verdict.Tier, verdict.Decision, result, duration, context, ct);
    }

    private async Task<string> CompleteToolCallAsync(
        RegisteredTool tool, string modelName, JsonElement arguments, RiskTier? tier, GuardDecision decision, Result<ToolOutput> result, int duration, MarshalExecutionContext context, CancellationToken ct)
    {
        int step = await RecordToolStepAsync(tool, modelName, arguments, tier, decision, [], result.IsSuccess ? result.Value : null, result.IsFailure ? result.Error : null, duration, ct);
        if (result.IsFailure)
            return ErrorText(result.Error);

        if (!tool.Descriptor.UntrustedOutput)
            return result.Value.Content;

        // Taint is set before the result enters the context and never cleared within the run.
        string kind = tool.Descriptor.TaintKind ?? tool.Id.Namespace;
        context.Taint.Mark(kind, step);
        return UntrustedContent.Wrap(kind, result.Value.Content);
    }

    private async Task<Result<ToolOutput>> InvokeAsync(RegisteredTool tool, JsonElement arguments, MarshalExecutionContext context, CancellationToken ct)
    {
        try
        {
            return await tool.Tool.InvokeAsync(new ToolInvocation(arguments, context), ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // A crashing tool must not end the turn; the model gets an error like for any expected failure.
            LogToolCrashed(logger, exception, tool.Id.ToString());
            return new Error("tool.failed", "The tool failed unexpectedly.", ErrorType.Failure);
        }
    }

    private async Task AppendConfirmationAsync(ConfirmationStep confirmation, MarshalExecutionContext context, List<ChatMessage> messages, CancellationToken ct)
    {
        string modelName = confirmation.Tool?.ModelName ?? confirmation.Action.Tool.ToString();
        using var document = JsonDocument.Parse(confirmation.Action.ArgumentsJson);
        JsonElement arguments = document.RootElement.Clone();
        if (confirmation.Result is not { } result)
        {
            await RecordToolStepAsync(confirmation.Tool, modelName, arguments, RiskTier.Confirm, confirmation.Decision, confirmation.Action.Reasons, null, null, 0, ct);
            return;
        }

        string callId = $"confirmed_{confirmation.Action.Id.Value:N}";
        var argumentMap = arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, modelName, argumentMap)]));

        string text;
        if (confirmation.Tool is null)
        {
            await RecordToolStepAsync(null, modelName, arguments, RiskTier.Confirm, confirmation.Decision, confirmation.Action.Reasons, null, result.IsFailure ? result.Error : null, 0, ct);
            text = ErrorText(result.Error);
        }
        else
        {
            text = await CompleteToolCallAsync(confirmation.Tool, modelName, arguments, RiskTier.Confirm, confirmation.Decision, result, 0, context, ct);
        }

        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, text)]));
    }

    private async Task<int> RecordToolStepAsync(
        RegisteredTool? tool, string modelName, JsonElement arguments, RiskTier? tier, GuardDecision decision,
        IReadOnlyList<ConfirmationReason> reasons, ToolOutput? output, Error? error, int duration, CancellationToken ct) =>
        await RecordAsync(new RunStepRecord
        {
            Kind = RunStepKind.ToolCall,
            Tool = tool?.Id ?? GuessId(modelName),
            RiskTier = tier,
            GuardDecision = decision,
            Reasons = reasons,
            Arguments = arguments.GetRawText(),
            Content = output?.Content,
            Reference = output?.Reference,
            Sensitive = tool?.Descriptor.Sensitive ?? false,
            DurationMs = duration,
            Error = error is null ? null : $"{error.Code}: {error.Message}",
        }, ct);

    // Unknown names are recorded with the id they would map to (first '_' separates the namespace).
    private static ToolId? GuessId(string modelName)
    {
        int separator = modelName.IndexOf('_', StringComparison.Ordinal);
        Result<ToolId> parsed = separator > 0 ? ToolId.Parse($"{modelName[..separator]}.{modelName[(separator + 1)..]}") : ToolId.Parse(modelName);
        return parsed.IsSuccess ? parsed.Value : null;
    }

    private Task<int> RecordAsync(RunStepRecord step, CancellationToken ct)
    {
        MarshalExecutionContext context = executionContext.Current ?? throw new UnreachableException("The run context is set for the whole loop.");
        return recorder.RecordStepAsync(context.RunId!.Value, step with { Depth = context.Depth }, ct);
    }

    private static string ErrorText(Error error) =>
        JsonSerializer.Serialize(new { error = new { code = error.Code, message = error.Message } });

    private static string PendingText(PendingActionRecord action)
    {
        string reasons = string.Join(" ", action.Reasons.Select(reason => reason.Sentence));
        return $"Confirmation pending. The action was NOT done yet. Ask the user to confirm: {action.Summary}. Include this reason in your question: {reasons}";
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} started ({Trigger})")]
    private static partial void LogRunStarted(ILogger logger, Guid runId, string trigger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} completed: {Outcome} {Reason}")]
    private static partial void LogRunCompleted(ILogger logger, Guid runId, RunOutcome outcome, string? reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool call {Tool}: {Decision}")]
    private static partial void LogToolDecision(ILogger logger, string tool, GuardDecision decision);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool {Tool} threw an exception")]
    private static partial void LogToolCrashed(ILogger logger, Exception exception, string tool);

    private sealed record LoopResult(string Reply, PendingActionRecord? Pending, RunOutcome Outcome, string? Reason);
}
