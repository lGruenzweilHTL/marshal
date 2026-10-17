using System.Diagnostics;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
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
using Marshal.Core.Models;
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
    ILogger<AgentRunner> logger,
    IApprovalQueue? approvals = null,
    IBackgroundHandoff? handoff = null,
    IToolRedaction? redaction = null,
    IToolCallSummarizer? summarizer = null) : IAgentRunner
{
    private static readonly JsonSerializerOptions _argumentJson = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    private readonly IToolRedaction _redaction = redaction ?? new SensitiveToolRedaction();
    private readonly IToolCallSummarizer _summarizer = summarizer ?? new DefaultToolCallSummarizer(redaction ?? new SensitiveToolRedaction());

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
        // Every run, subagents included, starts its tool loading from the core namespaces.
        MarshalExecutionContext context = request.Context with { RunId = runId, Tools = tools, LoadedTools = new LoadedToolNamespaces() };
        using IDisposable scope = executionContext.Use(context);
        LogRunStarted(logger, runId.Value, request.Trigger);

        using CancellationTokenSource latencyCap = request.LatencyCap is { } cap ? new CancellationTokenSource(cap, timeProvider) : new CancellationTokenSource();
        using CancellationTokenSource wallTime = request.Limits?.MaxDuration is { } duration ? new CancellationTokenSource(duration, timeProvider) : new CancellationTokenSource();
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, latencyCap.Token, wallTime.Token);
        var transcript = new Transcript(request.Events, timeProvider, request.RequestStartedAt ?? timeProvider.GetTimestamp());
        LoopResult result;
        try
        {
            result = await LoopAsync(request, context, transcript, linked.Token);
        }
        catch (OperationCanceledException) when (wallTime.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            result = BudgetExceeded(context);
        }
        catch (OperationCanceledException) when (latencyCap.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // An interactive turn continues as a background task from its last completed tool round.
            result = await HandOffAsync(request, context, transcript, "latency_cap", FixedReplies.Backgrounded(context.Language), cancellationToken)
                ?? await TooLongAsync(request, context, cancellationToken);
        }

        // A spoken filler is part of what the user heard.
        if (transcript.Filler is { } filler)
            result = result with { Reply = $"{filler} {result.Reply}" };

        bool continueConversation = result.Pending is not null;
        await SaveLoadedNamespacesAsync(context, nested, cancellationToken);
        await RecordAsync(new RunStepRecord { Kind = RunStepKind.Reply, Content = result.Reply, ContinueConversation = continueConversation, Artifacts = transcript.Artifacts.Count == 0 ? null : [.. transcript.Artifacts] }, cancellationToken);
        if (!nested)
            await recorder.CompleteRunAsync(runId, new RunCompletion(result.Outcome, result.Reason, context.Taint.Sources) { TimeToFirstDeltaMs = transcript.FirstDeltaMs }, cancellationToken);
        LogRunCompleted(logger, runId.Value, result.Outcome, result.Reason);
        return new AgentRunResult(runId, result.Reply, continueConversation, result.Outcome, result.Reason, result.Pending) { Data = result.Data };
    }

    // Later turns of the session keep what this turn loaded; the core namespaces load anyway.
    private async Task SaveLoadedNamespacesAsync(MarshalExecutionContext context, bool nested, CancellationToken ct)
    {
        if (nested || sessionTools is null || context.SessionId is not { } session || !context.LoadedTools.RetrievalActive)
            return;

        await sessionTools.SaveAsync(session, [.. context.LoadedTools.All.Where(item => item.Reason != LoadReasons.Core).Select(item => item.Name)], ct);
    }

    private static async Task<LoopResult> TooLongAsync(AgentRunRequest request, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        if (request.Events is not null)
            await request.Events.WriteAsync(new StatusEvent(FixedReplies.LatencyStatus(context.Language)), cancellationToken);

        return new LoopResult(FixedReplies.LatencyCap(context.Language), null, RunOutcome.Cancelled, "latency_cap");
    }

    // Null when the run cannot move: not an interactive top-level turn, no task system, or the handoff was refused.
    private async Task<LoopResult?> HandOffAsync(AgentRunRequest request, MarshalExecutionContext context, Transcript transcript, string reason, string reply, CancellationToken ct)
    {
        if (handoff is null || !context.IsInteractive || context.IsSubagent || request.UserText is null)
            return null;

        Result<Abstractions.Tasks.TaskId> task = await handoff.HandOffAsync(new BackgroundHandoff(context, request.UserText, transcript.Messages, reason), ct);
        if (task.IsFailure)
        {
            LogHandoffRefused(logger, context.RunId?.Value ?? Guid.Empty, task.Error.Code);
            return null;
        }

        LogHandedOff(logger, context.RunId?.Value ?? Guid.Empty, task.Value.Value, reason);
        return new LoopResult(reply, null, RunOutcome.Backgrounded, reason);
    }

    private async Task<LoopResult> LoopAsync(AgentRunRequest request, MarshalExecutionContext context, Transcript transcript, CancellationToken ct)
    {
        var contextRequest = new ContextRequest(context, request.UserText);
        string systemPrompt = await contextBuilder.BuildSystemPromptAsync(contextRequest, ct);
        await RecordAsync(new RunStepRecord
        {
            Notes = contextRequest.Notes.All,
            Kind = RunStepKind.Context,
            Content = systemPrompt,
            Input = request.UserText,
        }, ct);

        List<ChatMessage> messages = [new(ChatRole.System, systemPrompt), .. request.History];
        if (request.UserText is not null)
            messages.Add(new ChatMessage(ChatRole.User, request.UserText));
        if (request.Confirmation is not null)
            await AppendConfirmationAsync(request.Confirmation, context, messages, transcript, ct);
        transcript.Update(messages);

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
            ChatResponse response = await CallModelAsync(model, messages, visible, context, transcript, ct);
            messages.AddRange(response.Messages);

            List<FunctionCallContent> calls = [.. response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>()];
            if (calls.Count == 0)
                return new LoopResult(response.Text, pending, RunOutcome.Ok, null);

            if (request.Limits is { } limits && Exceeds(limits, context.Usage))
                return BudgetExceeded(context);

            if (rounds == maxSteps)
                return new LoopResult(FixedReplies.StepLimit(context.Language), pending, RunOutcome.Error, "step_limit");

            rounds++;
            HashSet<string> offered = [.. visible.Select(tool => tool.ModelName)];
            var results = new List<AIContent>(calls.Count);
            foreach (FunctionCallContent call in calls)
            {
                (string text, PendingActionRecord? frozen) = await HandleCallAsync(call, context, offered, pending, transcript, ct);
                pending ??= frozen;
                results.Add(new FunctionResultContent(call.CallId, text));
            }

            messages.Add(new ChatMessage(ChatRole.Tool, results));
            transcript.Update(messages);

            if (context.Control.BackgroundMessage is { } status)
            {
                return await HandOffAsync(request, context, transcript, "move_to_background", status, ct)
                    ?? new LoopResult(FixedReplies.BackgroundUnavailable(context.Language), null, RunOutcome.Error, "background_refused");
            }

            // A background task run does not wait for its approval: it ends, and the decision resumes the task.
            if (pending is not null && Suspends(context))
                return new LoopResult(FixedReplies.AwaitingApproval(context.Language), pending, RunOutcome.Ok, "awaiting_approval");
        }
    }

    private bool Suspends(MarshalExecutionContext context) => approvals is not null && context.TaskId is not null && !context.IsInteractive;

    // Tool calls need another model call; the budget must leave room for it.
    private static bool Exceeds(RunLimits limits, RunUsage usage) =>
        usage.Cost > limits.MaxCost || usage.Tokens > limits.MaxTokens || usage.ModelCalls >= limits.MaxSteps;

    private static LoopResult BudgetExceeded(MarshalExecutionContext context) =>
        new(FixedReplies.BudgetExceeded(context.Language), null, RunOutcome.Error, "budget_exceeded");

    private async Task<ChatResponse> CallModelAsync(
        RoutedModel model, List<ChatMessage> messages, IReadOnlyList<RegisteredTool> tools, MarshalExecutionContext context, Transcript transcript, CancellationToken ct)
    {
        var options = new ChatOptions
        {
            Tools = tools.Count == 0
                ? null
                : [.. tools.Select(tool => AIFunctionFactory.CreateDeclaration(tool.ModelName, tool.Descriptor.Description, tool.Descriptor.ParametersSchema))],
            Reasoning = ReasoningRequest.For(model, context),
        };

        long started = timeProvider.GetTimestamp();
        int? firstToken = null;
        var updates = new List<ChatResponseUpdate>();
        var reasoning = new System.Text.StringBuilder();
        await foreach (ChatResponseUpdate update in model.Client.GetStreamingResponseAsync(messages, options, ct))
        {
            updates.Add(update);
            string text = update.Text;
            if (firstToken is null && (text.Length > 0 || update.Contents.Any(content => content is FunctionCallContent or TextReasoningContent)))
                firstToken = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds;
            foreach (TextReasoningContent thought in update.Contents.OfType<TextReasoningContent>())
            {
                if (string.IsNullOrEmpty(thought.Text))
                    continue;

                reasoning.Append(thought.Text);
                await transcript.EmitAsync(new ReasoningEvent(thought.Text), ct);
            }

            if (text.Length > 0)
                await transcript.EmitAsync(new DeltaEvent(text), ct);
        }

        var response = updates.ToChatResponse();
        decimal cost = costs.Calculate(model.Key, response.Usage);
        RunUsage usage = context.Usage;
        usage.AddModelCall(cost, (response.Usage?.InputTokenCount ?? 0) + (response.Usage?.OutputTokenCount ?? 0));
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
            Cost = cost,
            Content = response.Text,
            // Reasoning after a redacted tool result may quote it, so it is not kept.
            Reasoning = transcript.WithholdsReasoning || reasoning.Length == 0 ? null : reasoning.ToString(),
            ReasoningWithheld = transcript.WithholdsReasoning && reasoning.Length > 0,
        }, ct);
        return response;
    }

    private async Task<(string Text, PendingActionRecord? Frozen)> HandleCallAsync(
        FunctionCallContent call, MarshalExecutionContext context, HashSet<string> offered, PendingActionRecord? pending, Transcript transcript, CancellationToken ct)
    {
        int loadMark = context.LoadedTools.All.Count;
        JsonElement arguments = JsonSerializer.SerializeToElement(call.Arguments ?? new Dictionary<string, object?>(), _argumentJson);
        string argumentSummary = _summarizer.Arguments(arguments);
        if (pending is not null)
        {
            var notNow = new Error("confirmation.already_pending", "Not executed: another action is waiting for confirmation. Ask about that one first.", ErrorType.Conflict);
            string unverified = GuessId(call.Name)?.ToString() ?? call.Name;
            await transcript.EmitAsync(new ToolEvent(call.CallId, unverified, argumentSummary, ToolEventState.Running), ct);
            await RecordToolStepAsync(null, call.Name, arguments, null, GuardDecision.Denied, [], null, notNow, 0, ct);
            await transcript.EmitAsync(new ToolEvent(call.CallId, unverified, argumentSummary, ToolEventState.Denied, 0, notNow.Code), ct);
            return (ErrorText(notNow), null);
        }

        await LoadOnCallAsync(call.Name, context, offered, ct);
        GuardVerdict verdict = await guard.EvaluateAsync(new GuardRequest(call.Name, arguments, context, offered), ct);
        string toolName = verdict.Tool?.Id.ToString() ?? GuessId(call.Name)?.ToString() ?? call.Name;
        LogToolDecision(logger, toolName, verdict.Decision);
        await transcript.EmitAsync(new ToolEvent(call.CallId, toolName, argumentSummary, ToolEventState.Running), ct);
        switch (verdict.Outcome)
        {
            case GuardOutcome.Deny:
                await RecordToolStepAsync(verdict.Tool, call.Name, arguments, verdict.Tier, GuardDecision.Denied, [], null, verdict.Denial, 0, ct, LoadNotes(context, loadMark));
                await transcript.EmitAsync(new ToolEvent(call.CallId, toolName, argumentSummary, ToolEventState.Denied, 0, verdict.Denial?.Code), ct);
                return (ErrorText(verdict.Denial!), null);

            case GuardOutcome.RequireConfirmation:
                RegisteredTool confirmTool = verdict.Tool!;
                string summary = confirmTool.Tool.Summarize(arguments, context.Language);
                var freeze = new FreezeRequest(
                    context.SessionId,
                    context.SatelliteId,
                    confirmTool.Id,
                    arguments.GetRawText(),
                    summary,
                    verdict.Reasons,
                    context.Language,
                    timeProvider.GetUtcNow() + confirmationOptions.Value.Ttl,
                    context.TaskId,
                    context.RunId,
                    context.Taint.Sources);
                PendingActionRecord frozen = Queues(context)
                    ? await approvals!.EnqueueAsync(freeze, ct)
                    : await pendingActions.FreezeAsync(freeze, ct);
                await RecordToolStepAsync(confirmTool, call.Name, arguments, verdict.Tier, verdict.Decision, verdict.Reasons, null, null, 0, ct, LoadNotes(context, loadMark));
                await transcript.EmitAsync(new ToolEvent(call.CallId, toolName, argumentSummary, ToolEventState.Pending, 0), ct);

                // Nobody in a run without a task can confirm: the action waits in the queue and the run goes on.
                if (Queues(context) && context.TaskId is null)
                    return (QueuedText(frozen), null);

                return (PendingText(frozen), frozen);

            default:
                return (await ExecuteAsync(verdict.Tool!, call, arguments, argumentSummary, verdict, context, transcript, loadMark, ct), null);
        }
    }

    // A visible tool of a namespace the run has not loaded (the model may know it from earlier sessions) loads the
    // namespace. Retrieval saves tokens; it is not a boundary, so refusing would only cost a round trip.
    private async Task LoadOnCallAsync(string modelName, MarshalExecutionContext context, HashSet<string> offered, CancellationToken ct)
    {
        if (selector is null || !context.LoadedTools.RetrievalActive || offered.Contains(modelName))
            return;

        RegisteredTool? tool = (context.Tools ?? registry.Current).FindByModelName(modelName);
        if (tool is null || tool.Id == NamespaceToolSelector.FindToolsId || context.LoadedTools.Contains(tool.Id.Namespace))
            return;

        if (!(await visibility.CheckAsync(tool, context, ct)).IsVisible)
            return;

        context.LoadedTools.Load(tool.Id.Namespace, LoadReasons.Call);
        offered.Add(modelName);
    }

    private static IReadOnlyList<ContextNote> LoadNotes(MarshalExecutionContext context, int mark) =>
        [.. context.LoadedTools.All.Skip(mark).Select(item => new ContextNote(LoadReasons.NoteKind, item.Name, item.Reason, item.Score))];

    // In a voice turn nothing has been said yet and a slow tool is next: speak its filler so the user hears something.
    private async Task SpeakFillerAsync(RegisteredTool tool, MarshalExecutionContext context, Transcript transcript, CancellationToken ct)
    {
        if (context.InputMode != InputMode.Voice || transcript.DeltaSent || !tool.Descriptor.Fillers.TryGetValue(context.Language, out string? line))
            return;

        int threshold = voiceOptions?.Value.FillerThresholdMs ?? new VoiceOptions().FillerThresholdMs;
        TimeSpan? expected = latency is null ? null : await latency.ExpectedDurationAsync(tool.Id, ct);
        if ((expected?.TotalMilliseconds ?? threshold + 1) <= threshold)
            return;

        transcript.Filler = line;
        await transcript.EmitAsync(new DeltaEvent(line), ct);
    }

    private async Task<string> ExecuteAsync(
        RegisteredTool tool, FunctionCallContent call, JsonElement arguments, string argumentSummary, GuardVerdict verdict, MarshalExecutionContext context, Transcript transcript, int loadMark, CancellationToken ct)
    {
        await SpeakFillerAsync(tool, context, transcript, ct);
        long started = timeProvider.GetTimestamp();
        Result<ToolOutput> result = await InvokeAsync(tool, arguments, context, ct);
        int duration = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds;
        string text = await CompleteToolCallAsync(tool, call.Name, arguments, verdict.Tier, verdict.Decision, result, duration, context, transcript, ct, loadMark);
        string? summary = _summarizer.Result(tool.Id, tool.Descriptor.Sensitive, result.IsSuccess ? result.Value.Content : null, result.IsFailure ? result.Error.Code : null);
        await transcript.EmitAsync(new ToolEvent(call.CallId, tool.Id.ToString(), argumentSummary, result.IsSuccess ? ToolEventState.Ok : ToolEventState.Error, duration, summary), ct);
        return text;
    }

    private async Task<string> CompleteToolCallAsync(
        RegisteredTool tool, string modelName, JsonElement arguments, RiskTier? tier, GuardDecision decision, Result<ToolOutput> result, int duration, MarshalExecutionContext context, Transcript transcript, CancellationToken ct, int? loadMark = null)
    {
        int step = await RecordToolStepAsync(
            tool, modelName, arguments, tier, decision, [], result.IsSuccess ? result.Value : null, result.IsFailure ? result.Error : null, duration, ct, loadMark is { } mark ? LoadNotes(context, mark) : null);
        if (result.IsFailure)
            return ErrorText(result.Error);

        if (_redaction.Redacts(tool.Id, tool.Descriptor.Sensitive))
            transcript.WithholdsReasoning = true;
        await transcript.ShowAsync(result.Value.Artifacts, ct);
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

    private async Task AppendConfirmationAsync(ConfirmationStep confirmation, MarshalExecutionContext context, List<ChatMessage> messages, Transcript transcript, CancellationToken ct)
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
            text = await CompleteToolCallAsync(confirmation.Tool, modelName, arguments, RiskTier.Confirm, confirmation.Decision, result, 0, context, transcript, ct);
        }

        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, text)]));
    }

    private async Task<int> RecordToolStepAsync(
        RegisteredTool? tool, string modelName, JsonElement arguments, RiskTier? tier, GuardDecision decision,
        IReadOnlyList<ConfirmationReason> reasons, ToolOutput? output, Error? error, int duration, CancellationToken ct, IReadOnlyList<ContextNote>? notes = null) =>
        await RecordAsync(new RunStepRecord
        {
            Notes = notes ?? [],
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} continues as background task {TaskId} ({Reason})")]
    private static partial void LogHandedOff(ILogger logger, Guid runId, Guid taskId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId} could not move to the background: {Code}")]
    private static partial void LogHandoffRefused(ILogger logger, Guid runId, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} started ({Trigger})")]
    private static partial void LogRunStarted(ILogger logger, Guid runId, string trigger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} completed: {Outcome} {Reason}")]
    private static partial void LogRunCompleted(ILogger logger, Guid runId, RunOutcome outcome, string? reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool call {Tool}: {Decision}")]
    private static partial void LogToolDecision(ILogger logger, string tool, GuardDecision decision);

    [LoggerMessage(Level = LogLevel.Error, Message = "Tool {Tool} threw an exception")]
    private static partial void LogToolCrashed(ILogger logger, Exception exception, string tool);

    private sealed record LoopResult(string Reply, PendingActionRecord? Pending, RunOutcome Outcome, string? Reason);

    // The run's messages after the system prompt as of the last completed tool round: what a background
    // continuation resumes from (never a tool call without its result).
    private sealed class Transcript(System.Threading.Channels.ChannelWriter<TurnEvent>? events)
    {
        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

        /// <summary>Artifacts the turn showed, kept with the reply.</summary>
        public List<ArtifactRef> Artifacts { get; } = [];

        /// <summary>A redacted tool result entered the run; later reasoning may quote it and is not recorded.</summary>
        public bool WithholdsReasoning { get; set; }

        public void Update(List<ChatMessage> messages) => Messages = [.. messages.Skip(1)];

        /// <summary>The filler line spoken before a slow tool, if any.</summary>
        public string? Filler { get; set; }

        /// <summary>True once a <c>delta</c> was sent (model text, filler or a fixed reply).</summary>
        public bool DeltaSent => FirstDeltaMs is not null;

        /// <summary>Milliseconds from the request to the first <c>delta</c>.</summary>
        public int? FirstDeltaMs { get; private set; }

        public async ValueTask EmitAsync(TurnEvent turnEvent, CancellationToken ct)
        {
            if (turnEvent is DeltaEvent && FirstDeltaMs is null)
                FirstDeltaMs = (int)time.GetElapsedTime(requestStarted).TotalMilliseconds;
            if (events is not null)
                await events.WriteAsync(turnEvent, ct);
        }

        public async Task ShowAsync(IReadOnlyList<ArtifactRef>? artifacts, CancellationToken ct)
        {
            foreach (ArtifactRef artifact in artifacts ?? [])
            {
                Artifacts.Add(artifact);
                if (events is not null)
                    await events.WriteAsync(new ArtifactEvent(artifact), ct);
            }
        }
    }
}
