using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Suggestions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Guard;
using Marshal.Tasks.Scheduling;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Suggestions;

/// <summary>
/// Runs a detector for every owner with "help Marshal learn" on (unless the detector is disabled or the owner muted
/// it), drops candidates whose dedupe key was sent within the cooldown, asks the policy whether and where to send,
/// has the writer word the facts and freezes a proposed action until the user accepts. A pushed suggestion goes
/// through the notification router (priority <c>normal</c>, push only, never voice, quiet hours apply) with Accept /
/// Dismiss actions; an inbox suggestion is only stored, the Inbox lists it from the store.
/// </summary>
public sealed partial class SuggestionPipeline(
    IUserDirectory users,
    IUserPreferencesStore preferences,
    ISuggestionStore store,
    ISuggestionPolicy policy,
    ISuggestionWriter writer,
    INotificationSender notifications,
    IPendingActionStore pendingActions,
    IToolRegistry registry,
    IRiskEngine riskEngine,
    IExecutionContextAccessor executionContext,
    IOptions<SuggestionOptions> options,
    TimeProvider time,
    ILogger<SuggestionPipeline> logger)
{
    /// <summary>Prefix of the push action that accepts (followed by the nonce).</summary>
    public const string AcceptPrefix = "MARSHAL_SUGGEST_ACCEPT_";

    /// <summary>Prefix of the push action that dismisses (followed by the nonce).</summary>
    public const string DismissPrefix = "MARSHAL_SUGGEST_DISMISS_";

    /// <summary>Notification source prefix (followed by the detector id).</summary>
    public const string SourcePrefix = "suggestion:";

    /// <summary>Evaluates <paramref name="detector"/> for every owner; returns how many suggestions were sent.</summary>
    public async Task<int> EvaluateAsync(ISuggestionDetector detector, MarshalEvent? @event, CancellationToken cancellationToken)
    {
        SuggestionDetectorOptions settings = options.Value.For(detector.Id);
        if (!settings.Enabled)
            return 0;

        int sent = 0;
        foreach (OwnerId owner in await users.ListAsync(cancellationToken))
        {
            UserPreferencesRecord prefs = await preferences.GetAsync(owner, cancellationToken);
            if (!prefs.HelpMarshalLearn)
                continue;

            using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(owner) with { Language = prefs.Language });
            try
            {
                sent += await EvaluateForOwnerAsync(detector, settings, owner, prefs, @event, cancellationToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogDetectorFailed(exception, detector.Id, owner);
            }
        }

        return sent;
    }

    /// <summary>
    /// Expires open suggestions of every owner that passed their expiry or whose detector's condition no longer
    /// holds; expired ones count neither way.
    /// </summary>
    public async Task ExpireAsync(IEnumerable<ISuggestionDetector> detectors, CancellationToken cancellationToken)
    {
        var byId = detectors.ToDictionary(detector => detector.Id, StringComparer.Ordinal);
        DateTimeOffset now = time.GetUtcNow();
        foreach ((Guid id, OwnerId owner, string detector, string dedupeKey, DateTimeOffset expiresAt) in await store.ListAllOpenAsync(cancellationToken))
        {
            using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(owner));
            bool expired = expiresAt <= now;
            if (!expired && byId.TryGetValue(detector, out ISuggestionDetector? found))
            {
                try
                {
                    expired = !await found.StillHoldsAsync(owner, dedupeKey, cancellationToken);
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    LogDetectorFailed(exception, detector, owner);
                }
            }

            if (!expired)
                continue;

            Result<SuggestionRecord> decided = await store.DecideAsync(id, SuggestionStatus.Expired, now, cancellationToken);
            if (decided.IsSuccess && decided.Value.PendingActionId is { } action)
                await pendingActions.DecideAsync(action, PendingActionStatus.Expired, DecidedBy.System, cancellationToken);
        }
    }

    private async Task<int> EvaluateForOwnerAsync(
        ISuggestionDetector detector, SuggestionDetectorOptions settings, OwnerId owner, UserPreferencesRecord prefs, MarshalEvent? @event, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        if ((await store.GetStatsAsync(detector.Id, cancellationToken)).IsMuted(now))
            return 0;

        IReadOnlyList<SuggestionCandidate> candidates = await detector.EvaluateAsync(new SuggestionContext(owner, prefs.Language, TimeZone(prefs.TimeZone), @event), cancellationToken);
        int sent = 0;
        foreach (SuggestionCandidate candidate in candidates)
        {
            if (await store.SentSinceAsync(detector.Id, candidate.DedupeKey, now - settings.Cooldown, cancellationToken))
                continue;

            SuggestionDelivery delivery = policy.Decide(await store.GetStatsAsync(detector.Id, cancellationToken));
            if (delivery == SuggestionDelivery.Drop)
            {
                LogDropped(detector.Id, owner);
                continue;
            }

            Result<string> text = await writer.WriteAsync(detector.Id, candidate.Facts, prefs.Language, cancellationToken);
            if (text.IsFailure)
            {
                LogNotWorded(detector.Id, text.Error.Code);
                continue;
            }

            await SendAsync(detector.Id, owner, prefs.Language, candidate, delivery, text.Value, now, cancellationToken);
            sent++;
        }

        return sent;
    }

    private async Task SendAsync(
        string detector, OwnerId owner, string language, SuggestionCandidate candidate, SuggestionDelivery delivery, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        DateTimeOffset expiresAt = now + options.Value.Expiry;
        PendingActionRecord? frozen = candidate.Action is { } action ? await FreezeAsync(action, language, expiresAt, cancellationToken) : null;
        var record = new SuggestionRecord(
            Guid.CreateVersion7(now),
            owner,
            detector,
            candidate.DedupeKey,
            text,
            candidate.Facts,
            delivery,
            SuggestionStatus.Open,
            Convert.ToHexStringLower(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)),
            frozen?.Id,
            frozen?.Summary,
            now,
            expiresAt,
            Snippet: candidate.Snippet);
        await store.AddAsync(record, cancellationToken);
        if (delivery != SuggestionDelivery.Push)
            return;

        bool german = language.StartsWith("de", StringComparison.OrdinalIgnoreCase);
        string reasons = frozen is null ? "" : string.Join(" ", frozen.Reasons.Select(reason => reason.Sentence));
        Result<Guid> notified = await notifications.SendAsync(new NotificationRequest
        {
            Owner = owner,
            Source = SourcePrefix + detector,
            Priority = NotificationPriority.Normal,
            Channel = DeliveryChannel.Push,
            Text = reasons.Length == 0 ? text : $"{text} {reasons}",
            PendingActionId = frozen?.Id,
            Link = "inbox",
            Tag = $"suggestion-{record.Id}",
            Actions =
            [
                new NotificationAction(AcceptPrefix + record.Nonce, AcceptTitle(frozen, german)),
                new NotificationAction(DismissPrefix + record.Nonce, german ? "Verwerfen" : "Dismiss"),
            ],
        }, cancellationToken);
        if (notified.IsSuccess)
            await store.SetNotificationAsync(record.Id, notified.Value, cancellationToken);
        else
            LogNotifyFailed(record.Id, notified.Error.Message);
    }

    private static string AcceptTitle(PendingActionRecord? action, bool german) => action is null
        ? german ? "Annehmen" : "Accept"
        : german ? "Ja, ausführen" : "Yes, do it";

    // Frozen like an approval, not queued: only an Accept of this suggestion runs it. A tool that is gone or forbidden
    // leaves the suggestion without an action.
    private async Task<PendingActionRecord?> FreezeAsync(ProposedAction action, string language, DateTimeOffset expiresAt, CancellationToken cancellationToken)
    {
        RegisteredTool? tool = registry.Current.FindById(action.Tool);
        if (tool is null)
        {
            LogActionUnavailable(action.Tool);
            return null;
        }

        RiskAssessment assessment = await riskEngine.AssessAsync(tool, action.Arguments, cancellationToken);
        if (assessment.Tier == RiskTier.Forbidden)
        {
            LogActionUnavailable(action.Tool);
            return null;
        }

        IReadOnlyList<ConfirmationReason> reasons = assessment.Tier == RiskTier.Confirm
            ? [.. assessment.Reasons.DefaultIfEmpty(ConfirmationReasonCode.Rule).Select(code => code switch
            {
                ConfirmationReasonCode.UnresolvedTarget => ConfirmationReasonText.UnresolvedTarget(language),
                ConfirmationReasonCode.QuietHoursOverride => ConfirmationReasonText.QuietHoursOverride(language),
                _ => ConfirmationReasonText.Rule(action.Summary, language),
            })]
            : [];
        return await pendingActions.FreezeAsync(
            new FreezeRequest(null, null, action.Tool, action.Arguments.GetRawText(), action.Summary, reasons, language, expiresAt),
            cancellationToken);
    }

    private static TimeZoneInfo TimeZone(string id)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(id);
        }
        catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suggestion detector {Detector} failed for owner {Owner}")]
    private partial void LogDetectorFailed(Exception exception, string detector, OwnerId owner);

    [LoggerMessage(Level = LogLevel.Debug, Message = "Suggestion of {Detector} for owner {Owner} dropped by the acceptance estimate")]
    private partial void LogDropped(string detector, OwnerId owner);

    [LoggerMessage(Level = LogLevel.Information, Message = "Suggestion of {Detector} not worded: {Code}")]
    private partial void LogNotWorded(string detector, string code);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Suggestion {SuggestionId} was stored, but its push failed: {Reason}")]
    private partial void LogNotifyFailed(Guid suggestionId, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Proposed action {Tool} is not available; the suggestion is sent without it")]
    private partial void LogActionUnavailable(ToolId tool);
}
