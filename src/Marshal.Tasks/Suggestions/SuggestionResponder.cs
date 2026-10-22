using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Suggestions;
using Marshal.Abstractions.Tools;
using Marshal.Core.Guard;
using Marshal.Tasks.Scheduling;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Suggestions;

/// <summary>What answering a suggestion did.</summary>
/// <param name="Suggestion">The answered suggestion.</param>
/// <param name="ActionResult">The proposed action's result or error text; null when nothing ran.</param>
public sealed record SuggestionAnswer(SuggestionRecord Suggestion, string? ActionResult);

/// <summary>
/// Accept and Dismiss, from the Inbox (as the current owner) or a nonce-verified push action. Accept runs the frozen
/// proposed action once through the guard as the owner; Accept is a user action, so a confirm-tier action counts as
/// confirmed. Dismiss runs nothing. Both count for the detector's estimate.
/// </summary>
public sealed partial class SuggestionResponder(
    ISuggestionStore store,
    IPendingActionStore pendingActions,
    IToolRegistry registry,
    IRiskEngine riskEngine,
    INotificationSender notifications,
    IExecutionContextAccessor executionContext,
    TimeProvider time,
    ILogger<SuggestionResponder> logger)
{
    private const int MaxResultLength = 300;

    /// <summary>Answers the current owner's open suggestion.</summary>
    public async Task<Result<SuggestionAnswer>> AnswerAsync(Guid id, bool accept, DecidedBy decidedBy, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        SuggestionRecord? open = await store.GetAsync(id, cancellationToken);
        if (open is null)
            return SuggestionErrors.NotFound();
        if (open.Status == SuggestionStatus.Open && open.ExpiresAt <= now)
        {
            await store.DecideAsync(id, SuggestionStatus.Expired, now, cancellationToken);
            if (open.PendingActionId is { } stale)
                await pendingActions.DecideAsync(stale, PendingActionStatus.Expired, DecidedBy.System, cancellationToken);
            return SuggestionErrors.AlreadyDecided();
        }

        Result<SuggestionRecord> decided = await store.DecideAsync(id, accept ? SuggestionStatus.Accepted : SuggestionStatus.Dismissed, now, cancellationToken);
        if (decided.IsFailure)
            return decided.Error;

        LogAnswered(id, accept ? "accepted" : "dismissed", decidedBy);
        if (decided.Value.PendingActionId is not { } actionId)
            return new SuggestionAnswer(decided.Value, null);

        if (!accept)
        {
            await pendingActions.DecideAsync(actionId, PendingActionStatus.Rejected, decidedBy, cancellationToken);
            return new SuggestionAnswer(decided.Value, null);
        }

        PendingActionRecord? action = await pendingActions.GetAsync(actionId, cancellationToken);
        if (action is null || (await pendingActions.DecideAsync(actionId, PendingActionStatus.Approved, decidedBy, cancellationToken)).IsFailure)
            return new SuggestionAnswer(decided.Value, null);

        var context = new MarshalExecutionContext
        {
            OwnerId = action.OwnerId,
            Source = SessionSource.Task,
            InputMode = InputMode.Text,
            Language = action.Language,
            IsInteractive = false,
        };
        Result<ToolOutput> result;
        using (executionContext.Use(context))
            (_, result) = await FrozenActions.ExecuteAsync(registry, riskEngine, action, context, cancellationToken);

        return new SuggestionAnswer(decided.Value, result.IsSuccess ? Truncate(result.Value.Content) : result.Error.Message);
    }

    /// <summary>
    /// Handles a <c>mobile_app_notification_action</c> identifier. Only an accept / dismiss action with the nonce of an
    /// open suggestion is taken, answered as that suggestion's owner; anything else is logged and ignored. A proposed
    /// action's outcome is sent back as a <c>low</c> notification.
    /// </summary>
    /// <returns>True when the suggestion was answered.</returns>
    public async Task<bool> HandlePushActionAsync(string action, CancellationToken cancellationToken)
    {
        bool accept = action.StartsWith(SuggestionPipeline.AcceptPrefix, StringComparison.Ordinal);
        if (!accept && !action.StartsWith(SuggestionPipeline.DismissPrefix, StringComparison.Ordinal))
            return false;

        string nonce = action[(accept ? SuggestionPipeline.AcceptPrefix : SuggestionPipeline.DismissPrefix).Length..];
        if (nonce.Length == 0 || await store.FindByNonceAsync(nonce, cancellationToken) is not { } found)
        {
            LogUnknownNonce();
            return false;
        }

        using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(found.Owner));
        Result<SuggestionAnswer> answered = await AnswerAsync(found.Id, accept, DecidedBy.Push, cancellationToken);
        if (answered.IsFailure)
        {
            LogPushRefused(found.Id, answered.Error.Code);
            return false;
        }

        if (answered.Value.ActionResult is { } outcome && answered.Value.Suggestion.ActionSummary is { } summary)
        {
            await notifications.SendAsync(new NotificationRequest
            {
                Owner = found.Owner,
                Source = SuggestionPipeline.SourcePrefix + answered.Value.Suggestion.Detector,
                Priority = NotificationPriority.Low,
                Text = $"{summary}: {outcome}",
            }, cancellationToken);
        }

        return true;
    }

    private static string Truncate(string content) => content.Length > MaxResultLength ? content[..MaxResultLength] + "…" : content;

    [LoggerMessage(Level = LogLevel.Information, Message = "Suggestion {SuggestionId}: {Answer} by {DecidedBy}")]
    private partial void LogAnswered(Guid suggestionId, string answer, DecidedBy decidedBy);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Push suggestion action with an unknown nonce ignored")]
    private partial void LogUnknownNonce();

    [LoggerMessage(Level = LogLevel.Information, Message = "Push answer for suggestion {SuggestionId} refused: {Code}")]
    private partial void LogPushRefused(Guid suggestionId, string code);
}
