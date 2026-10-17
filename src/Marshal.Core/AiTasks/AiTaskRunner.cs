using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Templates;
using Marshal.Core.Agent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.AiTasks;

/// <summary>
/// <see cref="IAiTaskRunner"/>: a one-shot <c>automation</c> session per task, the <c>ai_task</c> template, text
/// output, non-interactive (confirm-tier calls go to the approval queue), limited by <c>aiTasks.timeoutSeconds</c>
/// and what is left of the daily cost cap.
/// </summary>
public sealed class AiTaskRunner(
    IAgentRunner runner,
    ISessionStore sessions,
    IUserProfileProvider profiles,
    IDailyCostCap cap,
    IExecutionContextAccessor executionContext,
    IOptions<AiTaskOptions> options) : IAiTaskRunner
{
    private const string ResolvedBy = "ai_task";
    private static readonly string[] _supportedLanguages = ["de", "en"];

    public async Task<Result<AiTaskResult>> RunAsync(AiTaskRequest request, CancellationToken cancellationToken)
    {
        decimal left = await cap.LeftTodayAsync(request.Owner, cancellationToken);
        if (left <= 0)
            return DailyCap();

        var context = new MarshalExecutionContext
        {
            OwnerId = request.Owner,
            Source = SessionSource.Automation,
            InputMode = InputMode.Text,
            Language = await LanguageAsync(request, cancellationToken),
            IsInteractive = false,
            Template = AgentTemplate.AiTaskName,
        };

        SessionRecord session;
        using (executionContext.Use(context))
            session = await sessions.CreateAsync(new NewSession(SessionSource.Automation, null, null, new IdentityEvidence(null, null, null, null), ResolvedBy), cancellationToken);

        context = context with { SessionId = session.Id };
        AgentRunResult result;
        try
        {
            using IDisposable scope = executionContext.Use(context);
            result = await runner.RunAsync(new AgentRunRequest
            {
                Context = context,
                Trigger = $"ai_task:{request.TaskName}",
                UserText = request.Instructions,
                UserAttachments = [.. request.Attachments.Select(attachment => new DataContent(attachment.Data, attachment.MediaType))],
                Structure = request.Structure,
                LatencyCap = TimeSpan.FromSeconds(options.Value.TimeoutSeconds),
                Limits = new RunLimits(MaxCost: left),
            }, cancellationToken);
        }
        finally
        {
            using (executionContext.Use(context))
                await sessions.CloseAsync(session.Id, CancellationToken.None);
        }

        return result switch
        {
            { Outcome: RunOutcome.Ok, Data: { } data } => new AiTaskResult(result.RunId, data),
            { Outcome: RunOutcome.Ok } => new AiTaskResult(result.RunId, JsonSerializer.SerializeToElement(result.Reply)),
            { OutcomeReason: "latency_cap" } => new Error(AiTaskErrors.TimeoutCode, $"The task did not finish within {options.Value.TimeoutSeconds} seconds.", ErrorType.Unavailable),
            { OutcomeReason: "budget_exceeded" } => DailyCap(),
            { OutcomeReason: AgentRunRequest.NoVisionModelReason } => new Error(AiTaskErrors.NoVisionModelCode, "No model with image input is configured for the task's tier.", ErrorType.Validation),
            { OutcomeReason: AgentRunRequest.InvalidOutputReason } => new Error(StructuredOutput.InvalidOutputCode, "The answer could not be shaped into the requested structure.", ErrorType.Failure),
            _ => new Error(AiTaskErrors.FailedCode, $"The task run ended with {result.OutcomeReason ?? result.Outcome.ToString().ToLowerInvariant()}.", ErrorType.Failure),
        };
    }

    private static Error DailyCap() => new(AiTaskErrors.DailyCapCode, "The daily cost cap is reached.", ErrorType.Conflict);

    private async Task<string> LanguageAsync(AiTaskRequest request, CancellationToken cancellationToken)
    {
        string? requested = request.Language is { Length: >= 2 } language ? language[..2].ToLowerInvariant() : null;
        if (requested is not null && _supportedLanguages.Contains(requested))
            return requested;

        Result<UserProfile> profile = await profiles.GetAsync(request.Owner, cancellationToken);
        return profile.IsSuccess && _supportedLanguages.Contains(profile.Value.Language) ? profile.Value.Language : "en";
    }
}
