using Marshal.Abstractions.Context;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Guard;
using Marshal.Core.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Evals;

/// <summary>
/// Replays an eval case through Marshal's agent loop with the candidate configuration: the case's history, input and
/// turn context, the current static context (with the candidate's template and base prompt), and the candidate's
/// models. Every tool answers from the recording (<see cref="RecordedToolExecutor"/>), the recorder and pending
/// actions stay in memory, and the clock stands at the case's time, so a replay writes nothing.
/// </summary>
public sealed class AgentLoopReplayer(IServiceProvider services, EvalModels models, IToolRegistry registry, IExecutionContextAccessor executionContext) : IEvalReplayer
{
    public async Task<ReplayResult> ReplayAsync(EvalCase evalCase, CandidateConfig candidate, CancellationToken cancellationToken)
    {
        OwnerIdOrThrow();
        var executor = new RecordedToolExecutor(evalCase.ToolCalls);
        var recorder = new ReplayRunRecorder();
        var clock = new FixedTimeProvider(evalCase.RecordedAt);
        AgentRunner runner = Runner(models.RouterFor(candidate.Models), recorder, new ReplayPendingActions(executionContext.Current!.OwnerId, clock), clock);

        ToolSnapshot live = registry.Current;
        var tools = new ToolSnapshot([.. live.Tools.Select(tool => tool with { Tool = new RecordedTool(tool.Tool, executor) })], live.Errors, live.Namespaces);
        var context = new MarshalExecutionContext
        {
            OwnerId = executionContext.Current!.OwnerId,
            Source = evalCase.InputMode == InputMode.Voice ? SessionSource.Voice : SessionSource.Web,
            InputMode = evalCase.InputMode,
            Language = evalCase.Language,
            SatelliteId = evalCase.SatelliteId,
            AreaId = evalCase.AreaId,
            Template = candidate.Template ?? AgentTemplate.DefaultName,
            Tools = tools,
            // Confirm-tier calls end in a confirmation question, as in a live turn; nobody answers it.
            IsInteractive = true,
            Replay = new ReplaySettings(evalCase.TurnContext ?? "", candidate.BasePrompt),
        };

        try
        {
            AgentRunResult result = await runner.RunAsync(new AgentRunRequest
            {
                Context = context,
                Trigger = "eval",
                UserText = evalCase.Input,
                History = [.. evalCase.History.Select(message => new ChatMessage(message.Role == "user" ? ChatRole.User : ChatRole.Assistant, message.Text))],
            }, cancellationToken);
            string? error = result.Outcome == RunOutcome.Ok ? null : $"{result.Outcome}: {result.OutcomeReason}";
            return new ReplayResult(executor.Calls, result.Reply, recorder.Cost, error);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return new ReplayResult(executor.Calls, null, recorder.Cost, exception.Message);
        }
    }

    private void OwnerIdOrThrow()
    {
        if (executionContext.Current is null)
            throw new InvalidOperationException("A replay needs the owner's execution context.");
    }

    // The live runner's collaborators, except everything that persists or reaches out: recorder, pending actions,
    // approval queue, handoff, session tools, latency stats, widgets and apps.
    private AgentRunner Runner(IModelRouter router, IRunRecorder recorder, IPendingActionStore pendingActions, TimeProvider clock) =>
        new(
            router,
            registry,
            services.GetRequiredService<ITemplateCatalog>(),
            services.GetRequiredService<IContextBuilder>(),
            services.GetRequiredService<IToolVisibilityFilter>(),
            services.GetRequiredService<IGuard>(),
            pendingActions,
            recorder,
            services.GetRequiredService<ICostCalculator>(),
            executionContext,
            services.GetRequiredService<IOptions<AgentOptions>>(),
            services.GetRequiredService<IOptions<ConfirmationOptions>>(),
            clock,
            services.GetRequiredService<ILogger<AgentRunner>>(),
            redaction: services.GetService<IToolRedaction>(),
            summarizer: services.GetService<IToolCallSummarizer>(),
            selector: services.GetService<IToolSelector>(),
            voiceOptions: services.GetService<IOptions<VoiceOptions>>());
}
