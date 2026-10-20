using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Apps;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Abstractions.Widgets;
using Marshal.Core.AiTasks;
using Marshal.Core.Guard;
using Marshal.Core.Models;
using Marshal.Core.Recording;
using Marshal.Core.Tools;
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
    IToolCallSummarizer? summarizer = null,
    IToolSelector? selector = null,
    ISessionToolNamespaces? sessionTools = null,
    IToolLatencyStats? latency = null,
    IOptions<VoiceOptions>? voiceOptions = null,
    IStructuredOutputShaper? shaper = null,
    IToolCallPipeline? pipeline = null,
    IEnumerable<IWidgetDataProvider>? widgets = null,
    IAppInstanceStore? apps = null) : IAgentRunner
{
    private readonly IEnumerable<IWidgetDataProvider> _widgets = widgets ?? [];
    private static readonly JsonSerializerOptions _argumentJson = new(AIJsonUtilities.DefaultOptions) { WriteIndented = false };

    private readonly IToolCallSummarizer _summarizer = summarizer ?? new DefaultToolCallSummarizer(redaction ?? new SensitiveToolRedaction());
    private readonly IToolCallPipeline _pipeline = pipeline ?? new GuardedToolCallPipeline(
        guard, pendingActions, recorder, confirmationOptions, timeProvider, Microsoft.Extensions.Logging.Abstractions.NullLogger<GuardedToolCallPipeline>.Instance, approvals, redaction);

    public async Task<AgentRunResult> RunAsync(AgentRunRequest request, CancellationToken cancellationToken)
    {
        // A subagent records its steps in its caller's run, marked with its depth.
        bool nested = request.Context is { IsSubagent: true, RunId: not null };
        using Activity? span = RunTracing.StartRun(request.Context, request.Context.Template ?? AgentTemplate.DefaultName, request.Trigger);
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
        span?.SetTag("marshal.run_id", runId.ToString());
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
        catch (Exception exception) when (Traced(span, exception))
        {
            throw new UnreachableException();
        }

        // A spoken filler is part of what the user heard.
        if (transcript.Filler is { } filler)
            result = result with { Reply = $"{filler} {result.Reply}" };

        bool continueConversation = result.Pending is not null;
        await SaveLoadedNamespacesAsync(context, nested, cancellationToken);
        await RecordAsync(new RunStepRecord { Kind = RunStepKind.Reply, Content = result.Reply, ContinueConversation = continueConversation, Artifacts = transcript.Artifacts.Count == 0 ? null : [.. transcript.Artifacts], Widgets = transcript.Widgets.Count == 0 ? null : [.. transcript.Widgets], Apps = transcript.Apps.Count == 0 ? null : [.. transcript.Apps] }, cancellationToken);
        if (!nested)
            await recorder.CompleteRunAsync(runId, new RunCompletion(result.Outcome, result.Reason, context.Taint.Sources) { TimeToFirstDeltaMs = transcript.FirstDeltaMs }, cancellationToken);
        RunTracing.CompleteRun(span, result.Outcome, result.Reason, context.Taint.Sources.Count > 0);
        LogRunCompleted(logger, runId.Value, result.Outcome, result.Reason);
        return new AgentRunResult(runId, result.Reply, continueConversation, result.Outcome, result.Reason, result.Pending) { Data = result.Data };
    }

    // Marks the run span failed and lets the exception pass.
    private static bool Traced(Activity? span, Exception exception)
    {
        RunTracing.Fail(span, exception);
        return false;
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
        long contextStarted = timeProvider.GetTimestamp();
        var contextRequest = new ContextRequest(context, request.UserText);
        SystemPrompt prompt = await contextBuilder.BuildAsync(contextRequest, ct);
        string systemPrompt = prompt.Text;
        if (selector is not null && request.FixedReply is null)
        {
            IReadOnlyList<string> session = !context.IsSubagent && sessionTools is not null && context.SessionId is { } sessionId
                ? await sessionTools.GetAsync(sessionId, ct)
                : [];
            await selector.PrepareAsync(await visibility.GetVisibleToolsAsync(context, ct), new ToolSelectionRequest(contextRequest, session), ct);
        }

        await RecordAsync(new RunStepRecord
        {
            Notes = [.. contextRequest.Notes.All, .. request.UserAttachments.Select(AttachmentNote)],
            Kind = RunStepKind.Context,
            Content = systemPrompt,
            DurationMs = (int)timeProvider.GetElapsedTime(contextStarted).TotalMilliseconds,
            Input = request.UserText,
        }, ct);

        // Two parts, so a provider adapter can cache the stable part across the session's turns.
        List<ChatMessage> messages = [SystemMessage(prompt), .. request.History];
        if (request.UserAttachments.Count > 0)
            messages.Add(new ChatMessage(ChatRole.User, [.. request.UserText is null ? [] : new AIContent[] { new TextContent(request.UserText) }, .. request.UserAttachments]));
        else if (request.UserText is not null)
            messages.Add(new ChatMessage(ChatRole.User, request.UserText));
        if (request.Confirmation is not null)
            await AppendConfirmationAsync(request.Confirmation, context, messages, transcript, ct);
        transcript.Update(messages);

        if (request.FixedReply is not null)
        {
            await transcript.EmitAsync(new DeltaEvent(request.FixedReply), ct);
            return new LoopResult(request.FixedReply, null, RunOutcome.Ok, null);
        }

        AgentTemplate template = templates.For(context);
        ModelTier tier = template.ModelTier ?? (context.InputMode == InputMode.Voice ? ModelTier.Fast : ModelTier.Standard);
        int maxSteps = template.MaxSteps ?? agentOptions.Value.MaxSteps;
        bool vision = request.UserAttachments.Count > 0;
        Result<RoutedModel> routed = router.Route(new ModelRequest(tier, Vision: vision, Tools: true));
        if (routed.IsFailure)
            return new LoopResult(FixedReplies.ModelUnavailable(context.Language), null, RunOutcome.Error, vision ? AgentRunRequest.NoVisionModelReason : "model_unavailable");

        // Images are untrusted input: the guard sees the taint from the first tool call on.
        if (vision)
            context.Taint.Mark(AgentRunRequest.AttachmentTaintKind, 0);

        RoutedModel model = routed.Value;
        PendingActionRecord? pending = null;
        int rounds = 0;
        int inputMark = context.Control.InputRequests.Count;
        while (true)
        {
            IReadOnlyList<RegisteredTool> visible = pending is null ? await OfferAsync(context, ct) : [];
            ChatResponse response = await CallModelAsync(model, messages, visible, context, transcript, ct);
            messages.AddRange(response.Messages);

            List<FunctionCallContent> calls = [.. response.Messages.SelectMany(message => message.Contents).OfType<FunctionCallContent>()];
            if (calls.Count == 0)
                return await FinishAsync(request, model, response.Text, pending, context, ct);

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

            // A server asked the user for input: a background run waits for the answer like for an approval; a voice
            // turn cannot show the form, so it sends the user to the web UI.
            if (context.Control.InputRequests.Count > inputMark)
            {
                foreach (InputRequestId id in context.Control.InputRequests.Skip(inputMark))
                    await transcript.EmitAsync(new InputRequestEvent(id), ct);
                inputMark = context.Control.InputRequests.Count;
                if (Suspends(context))
                    return new LoopResult(FixedReplies.AwaitingInput(context.Language), null, RunOutcome.Ok, "awaiting_input");
                if (context.InputMode == InputMode.Voice)
                    return new LoopResult(FixedReplies.InputInWebUi(context.Language), null, RunOutcome.Ok, "awaiting_input");
            }
        }
    }

    // A requested structure is shaped from the final reply with the run's model; those calls are steps of this run.
    private async Task<LoopResult> FinishAsync(AgentRunRequest request, RoutedModel model, string reply, PendingActionRecord? pending, MarshalExecutionContext context, CancellationToken ct)
    {
        if (request.Structure is not { } structure)
            return new LoopResult(reply, pending, RunOutcome.Ok, null);
        if (shaper is null)
            throw new InvalidOperationException("A structured run needs an IStructuredOutputShaper.");

        Result<JsonElement> shaped = await shaper.ShapeAsync(new StructuredOutputRequest(model, request.UserText ?? "", reply, structure), context, ct);
        return shaped.IsSuccess
            ? new LoopResult(reply, pending, RunOutcome.Ok, null) { Data = shaped.Value }
            : new LoopResult(reply, pending, RunOutcome.Error, AgentRunRequest.InvalidOutputReason);
    }

    private async Task<IReadOnlyList<RegisteredTool>> OfferAsync(MarshalExecutionContext context, CancellationToken ct)
    {
        IReadOnlyList<RegisteredTool> visible = await visibility.GetVisibleToolsAsync(context, ct);
        return selector is null ? visible : selector.Select(visible, context);
    }

    // The record keeps what was attached, never the bytes.
    private static ContextNote AttachmentNote(DataContent attachment) => new(
        AgentRunRequest.AttachmentTaintKind,
        attachment.MediaType,
        $"{attachment.Data.Length} bytes, sha256 {Convert.ToHexStringLower(SHA256.HashData(attachment.Data.Span))}");

    private static ChatMessage SystemMessage(SystemPrompt prompt) => prompt.Turn.Length == 0 || prompt.Stable.Length == 0
        ? new ChatMessage(ChatRole.System, prompt.Text)
        : new ChatMessage(ChatRole.System, [new TextContent(prompt.Stable), new TextContent(prompt.Turn)]);

    private bool Suspends(MarshalExecutionContext context) => Queues(context) && context.TaskId is not null;

    // A non-interactive run never holds a pending action in its session; its confirmations go to the approval queue.
    private bool Queues(MarshalExecutionContext context) => approvals is not null && !context.IsInteractive;

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
        using Activity? span = RunTracing.StartTool(call.Name, call.CallId);
        try
        {
            return await HandleTracedCallAsync(call, context, offered, pending, transcript, ct);
        }
        catch (Exception exception) when (Traced(span, exception))
        {
            throw new UnreachableException();
        }
    }

    private async Task<(string Text, PendingActionRecord? Frozen)> HandleTracedCallAsync(
        FunctionCallContent call, MarshalExecutionContext context, HashSet<string> offered, PendingActionRecord? pending, Transcript transcript, CancellationToken ct)
    {
        int loadMark = context.LoadedTools.All.Count;
        JsonElement arguments = JsonSerializer.SerializeToElement(call.Arguments ?? new Dictionary<string, object?>(), _argumentJson);
        string argumentSummary = _summarizer.Arguments(arguments);
        if (pending is not null)
        {
            var notNow = new Error("confirmation.already_pending", "Not executed: another action is waiting for confirmation. Ask about that one first.", ErrorType.Conflict);
            string unverified = ToolCallSteps.GuessId(call.Name)?.ToString() ?? call.Name;
            await transcript.EmitAsync(new ToolEvent(call.CallId, unverified, argumentSummary, ToolEventState.Running), ct);
            await RecordAsync(ToolCallSteps.Step(null, call.Name, arguments, null, GuardDecision.Denied, [], null, notNow, 0), ct);
            await transcript.EmitAsync(new ToolEvent(call.CallId, unverified, argumentSummary, ToolEventState.Denied, 0, notNow.Code), ct);
            return (ToolCallSteps.ErrorText(notNow), null);
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

    // Built-in widgets render in the web chat only, for successful calls.
    private async Task ShowWidgetAsync(RegisteredTool tool, string callId, JsonElement arguments, Result<ToolOutput> result, MarshalExecutionContext context, Transcript transcript, CancellationToken ct)
    {
        if (context.Source != SessionSource.Web || result.IsFailure || tool.Descriptor.Widget is not { } kind)
            return;

        IWidgetDataProvider? provider = _widgets.FirstOrDefault(candidate => candidate.Kind == kind);
        if (provider is null || await provider.GetAsync(new WidgetRequest(tool.Id, arguments, result.Value, context), ct) is not { } data)
            return;

        var widget = new WidgetRef(kind, callId, data);
        transcript.Widgets.Add(widget);
        await transcript.EmitAsync(new WidgetEvent(widget), ct);
    }

    // MCP apps render in the web chat only; the instance keeps HTML, arguments and result for a reload.
    private async Task ShowAppAsync(string callId, JsonElement arguments, Result<ToolOutput> result, MarshalExecutionContext context, Transcript transcript, CancellationToken ct)
    {
        if (apps is null || context.Source != SessionSource.Web || result.IsFailure || result.Value.App is not { } app)
            return;

        AppInstanceId id = await apps.CreateAsync(new NewAppInstance(context.OwnerId, context.SessionId, context.RunId!.Value, callId, arguments.GetRawText(), app, result.Value.CallResult), ct);
        var shown = new AppRef(id, app.Server, app.Tool, callId);
        transcript.Apps.Add(shown);
        await transcript.EmitAsync(new AppEvent(shown), ct);
    }

    // A redacted result makes later reasoning unrecordable; artifacts of a result are shown with the reply.
    private static async Task ShowAsync(ToolCallOutcome outcome, Transcript transcript, CancellationToken ct)
    {
        if (outcome.Result is not { IsSuccess: true } result)
            return;

        if (outcome.Redacted)
            transcript.WithholdsReasoning = true;
        await transcript.ShowAsync(result.Value.Artifacts, ct);
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

    private async Task AppendConfirmationAsync(ConfirmationStep confirmation, MarshalExecutionContext context, List<ChatMessage> messages, Transcript transcript, CancellationToken ct)
    {
        string modelName = confirmation.Tool?.ModelName ?? confirmation.Action.Tool.ToString();
        string callId = $"confirmed_{confirmation.Action.Id.Value:N}";
        using Activity? span = RunTracing.StartTool(modelName, callId);
        using var document = JsonDocument.Parse(confirmation.Action.ArgumentsJson);
        JsonElement arguments = document.RootElement.Clone();
        if (confirmation.Result is not { } result)
        {
            await RecordAsync(ToolCallSteps.Step(confirmation.Tool, modelName, arguments, RiskTier.Confirm, confirmation.Decision, confirmation.Action.Reasons, null, null, 0), ct);
            return;
        }

        var argumentMap = arguments.EnumerateObject().ToDictionary(property => property.Name, property => (object?)property.Value.Clone());
        messages.Add(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent(callId, modelName, argumentMap)]));

        string text;
        if (confirmation.Tool is null)
        {
            await RecordAsync(ToolCallSteps.Step(null, modelName, arguments, RiskTier.Confirm, confirmation.Decision, confirmation.Action.Reasons, null, result.IsFailure ? result.Error : null, 0), ct);
            text = ToolCallSteps.ErrorText(result.Error);
        }
        else
        {
            ToolCallOutcome outcome = await _pipeline.CompleteConfirmedAsync(new ConfirmedToolCall(confirmation.Tool, modelName, arguments, confirmation.Decision, result, context), ct);
            await ShowAsync(outcome, transcript, ct);
            text = outcome.Text;
        }

        messages.Add(new ChatMessage(ChatRole.Tool, [new FunctionResultContent(callId, text)]));
    }

    private Task<int> RecordAsync(RunStepRecord step, CancellationToken ct)
    {
        MarshalExecutionContext context = executionContext.Current ?? throw new UnreachableException("The run context is set for the whole loop.");
        return recorder.RecordStepAsync(context.RunId!.Value, step with { Depth = context.Depth }, ct);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} continues as background task {TaskId} ({Reason})")]
    private static partial void LogHandedOff(ILogger logger, Guid runId, Guid taskId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Run {RunId} could not move to the background: {Code}")]
    private static partial void LogHandoffRefused(ILogger logger, Guid runId, string code);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} started ({Trigger})")]
    private static partial void LogRunStarted(ILogger logger, Guid runId, string trigger);

    [LoggerMessage(Level = LogLevel.Information, Message = "Run {RunId} completed: {Outcome} {Reason}")]
    private static partial void LogRunCompleted(ILogger logger, Guid runId, RunOutcome outcome, string? reason);

    private sealed record LoopResult(string Reply, PendingActionRecord? Pending, RunOutcome Outcome, string? Reason)
    {
        public JsonElement? Data { get; init; }
    }

    // The run's messages after the system prompt as of the last completed tool round: what a background
    // continuation resumes from (never a tool call without its result).
    private sealed class Transcript(System.Threading.Channels.ChannelWriter<TurnEvent>? events, TimeProvider time, long requestStarted)
    {
        public IReadOnlyList<ChatMessage> Messages { get; private set; } = [];

        /// <summary>Artifacts the turn showed, kept with the reply.</summary>
        public List<ArtifactRef> Artifacts { get; } = [];

        /// <summary>Widgets the turn showed, kept with the reply.</summary>
        public List<WidgetRef> Widgets { get; } = [];

        /// <summary>MCP apps the turn showed, kept with the reply.</summary>
        public List<AppRef> Apps { get; } = [];

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
