using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Guard;

/// <summary>
/// Keyword match first (no model call), then the judge. Executes exactly the frozen tool and arguments; the model
/// cannot change them. A frozen call whose tool is gone or now forbidden is not executed.
/// </summary>
public sealed class ConfirmationFlow(
    IPendingActionStore store,
    IConfirmationKeywords keywords,
    IConfirmationJudge judge,
    IToolRegistry registry,
    IRiskEngine riskEngine,
    TimeProvider timeProvider) : IConfirmationFlow
{
    public async Task<ConfirmationOutcome> HandleUserTurnAsync(SessionId sessionId, string userText, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        PendingActionRecord? action = await store.GetPendingAsync(sessionId, cancellationToken);
        if (action is null)
            return new ConfirmationOutcome(ConfirmationOutcomeKind.NoPendingAction);

        bool keyword = keywords.IsConfirmation(userText, context.Language);
        JudgeVerdict verdict = keyword ? JudgeVerdict.Positive : await judge.JudgeAsync(action.Summary, userText, context.Language, cancellationToken);

        if (timeProvider.GetUtcNow() >= action.ExpiresAt)
        {
            await store.DecideAsync(action.Id, PendingActionStatus.Expired, DecidedBy.System, cancellationToken);
            return verdict switch
            {
                JudgeVerdict.Positive => new ConfirmationOutcome(ConfirmationOutcomeKind.Expired, action, GuardDecision.Expired),
                JudgeVerdict.Negative => new ConfirmationOutcome(ConfirmationOutcomeKind.Cancelled, action, GuardDecision.Expired),
                _ => new ConfirmationOutcome(ConfirmationOutcomeKind.Dropped, action, GuardDecision.Expired),
            };
        }

        DecidedBy decidedBy = keyword ? DecidedBy.Keyword : DecidedBy.Judge;
        switch (verdict)
        {
            case JudgeVerdict.Negative:
                await store.DecideAsync(action.Id, PendingActionStatus.Rejected, decidedBy, cancellationToken);
                return new ConfirmationOutcome(ConfirmationOutcomeKind.Cancelled, action, GuardDecision.JudgeNegative);
            case JudgeVerdict.Neutral or JudgeVerdict.Failed:
                await store.DecideAsync(action.Id, PendingActionStatus.Dropped, decidedBy, cancellationToken);
                return new ConfirmationOutcome(ConfirmationOutcomeKind.Dropped, action, GuardDecision.JudgeNeutral);
        }

        Result decided = await store.DecideAsync(action.Id, PendingActionStatus.Approved, decidedBy, cancellationToken);
        if (decided.IsFailure)
            return new ConfirmationOutcome(ConfirmationOutcomeKind.Dropped, action, GuardDecision.Denied);

        GuardDecision decision = keyword ? GuardDecision.ConfirmedByKeyword : GuardDecision.JudgePositive;
        (RegisteredTool? tool, Result<ToolOutput> result) = await ExecuteFrozenAsync(action, context, cancellationToken);
        return new ConfirmationOutcome(ConfirmationOutcomeKind.Executed, action, decision, tool, result);
    }

    private async Task<(RegisteredTool? Tool, Result<ToolOutput> Result)> ExecuteFrozenAsync(PendingActionRecord action, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        RegisteredTool? tool = registry.Current.FindById(action.Tool);
        if (tool is null)
            return (null, new Error("tool.unknown", $"The tool '{action.Tool}' is no longer available.", ErrorType.NotFound));

        using var document = JsonDocument.Parse(action.ArgumentsJson);
        JsonElement arguments = document.RootElement.Clone();
        RiskAssessment assessment = await riskEngine.AssessAsync(tool, arguments, cancellationToken);
        if (assessment.Tier == RiskTier.Forbidden)
            return (tool, new Error("tool.forbidden", "This action is no longer allowed.", ErrorType.Forbidden));

        Result<ToolOutput> result = await tool.Tool.InvokeAsync(new ToolInvocation(arguments, context), cancellationToken);
        return (tool, result);
    }
}
