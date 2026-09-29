using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Data.Notifications;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Notifications;

/// <summary>
/// <see cref="INotificationSender"/>. Every notification is recorded with its outcome, channel and reason. In order:
/// <c>critical</c> (system sources only) goes out by voice and push at once, ignoring everything else; <c>low</c> goes
/// to the inbox (or a silent push when push was requested); during quiet hours <c>normal</c> is held until they end or
/// sent as a silent push (unless the task's quiet-hours override is active); <c>normal</c> notifications within the
/// coalescing window are merged into one message (not those with actions or a follow-up); at delivery, voice DND,
/// presence (home: the occupied room's satellite; away: push) and the per-source and hourly voice caps pick voice or
/// push; a failed voice delivery is pushed, and without a push target the notification lands in the inbox.
/// </summary>
internal sealed class NotificationRouter(
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IUserPreferencesStore preferences,
    IPresenceProvider presence,
    IEnumerable<INotificationChannel> channels,
    IOptions<NotificationOptions> options,
    TimeProvider time) : INotificationSender, IDisposable
{
    private const int DueBatch = 50;

    private readonly IReadOnlyList<INotificationChannel> _channels = [.. channels];
    private readonly SemaphoreSlim _changed = new(0, 1);

    public async Task<Result<Guid>> SendAsync(NotificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Text))
            return Invalid("A notification needs a text.");
        if (request.Priority == NotificationPriority.Critical && !request.IsSystem)
            return Invalid("Only system sources send critical notifications; use normal or low.");

        using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(request.Owner));
        DateTimeOffset now = time.GetUtcNow();
        UserPreferencesRecord owner = await preferences.GetAsync(request.Owner, cancellationToken);
        var routing = NotificationRouting.From(request);

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        var row = new Notification
        {
            Id = Guid.CreateVersion7(),
            OwnerId = request.Owner,
            TaskId = request.TaskId,
            Source = request.Source,
            Priority = request.Priority,
            Content = request.Text.Trim(),
            Routing = TaskJson.Serialize(routing),
            CreatedAt = now,
        };

        if (request.Priority == NotificationPriority.Critical)
        {
            await DeliverAsync(db, row, routing, owner, row.Content, now, cancellationToken);
        }
        else if (request.Priority == NotificationPriority.Low)
        {
            if (routing.Channel == DeliveryChannel.Push)
                await PushAsync(row, routing, owner, row.Content, "low", silent: true, now, cancellationToken);
            else
                Inbox(row, "low");
        }
        else if (routing.Channel == DeliveryChannel.Inbox)
        {
            Inbox(row, "requested");
        }
        else if (routing.Channel == DeliveryChannel.Silent)
        {
            Finish(row, routing, "silent", "requested", now);
        }
        else if (QuietUntil(owner, routing, now) is { } quietEnd)
        {
            if (options.Value.QuietHoursMode == QuietHoursMode.Hold)
            {
                row.Outcome = NotificationOutcome.Held;
                row.Reason = "quiet_hours";
                row.DeliverAfter = quietEnd;
            }
            else
            {
                await PushAsync(row, routing, owner, row.Content, "quiet_hours", silent: true, now, cancellationToken);
            }
        }
        else if (routing.Coalescable && options.Value.CoalesceWindow > TimeSpan.Zero)
        {
            Notification? open = await OpenWindowAsync(db, routing, now, cancellationToken);
            if (open is not null)
            {
                row.Outcome = NotificationOutcome.Coalesced;
                row.CoalescedInto = open.Id;
                row.Reason = "coalesced";
            }
            else
            {
                row.DeliverAfter = now + options.Value.CoalesceWindow;
            }
        }
        else
        {
            await DeliverAsync(db, row, routing, owner, row.Content, now, cancellationToken);
        }

        db.Notifications.Add(row);
        await db.SaveChangesAsync(cancellationToken);
        if (row.DeliverAfter is not null)
            Signal();
        return row.Id;
    }

    public void Dispose() => _changed.Dispose();

    /// <summary>Delivers held and coalescing notifications of every owner whose time has come.</summary>
    internal async Task DeliverDueAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        List<(Guid Id, OwnerId Owner)> due;
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
        {
            due = [.. (await db.Notifications.IgnoreQueryFilters().AsNoTracking()
                    .Where(row => row.DeliverAfter != null && row.DeliverAfter <= now && (row.Outcome == NotificationOutcome.Pending || row.Outcome == NotificationOutcome.Held))
                    .OrderBy(row => row.DeliverAfter)
                    .Take(DueBatch)
                    .Select(row => new { row.Id, row.OwnerId })
                    .ToListAsync(cancellationToken))
                .Select(row => (row.Id, row.OwnerId))];
        }

        foreach ((Guid id, OwnerId owner) in due)
        {
            using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(owner));
            await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
            Notification? row = await db.Notifications.SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
            if (row is null || row.DeliverAfter is null)
                continue;

            List<string> merged = await db.Notifications
                .Where(candidate => candidate.CoalescedInto == id)
                .OrderBy(candidate => candidate.CreatedAt)
                .Select(candidate => candidate.Content)
                .ToListAsync(cancellationToken);
            string text = string.Join(" ", merged.Prepend(row.Content));
            NotificationRouting routing = TaskJson.Deserialize<NotificationRouting>(row.Routing);
            row.DeliverAfter = null;
            await DeliverAsync(db, row, routing, await preferences.GetAsync(owner, cancellationToken), text, now, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>The earliest time a held or coalescing notification is due.</summary>
    internal async Task<DateTimeOffset?> NextDueAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.Notifications.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.DeliverAfter != null && (row.Outcome == NotificationOutcome.Pending || row.Outcome == NotificationOutcome.Held))
            .OrderBy(row => row.DeliverAfter)
            .Select(row => row.DeliverAfter)
            .FirstOrDefaultAsync(cancellationToken);
    }

    /// <summary>Completes when a notification was scheduled for later, or after <paramref name="timeout"/>.</summary>
    internal async Task WaitForChangeAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timedOut = new CancellationTokenSource(timeout, time);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timedOut.Token);
        try
        {
            await _changed.WaitAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Timed out.
        }
    }

    private void Signal()
    {
        try
        {
            if (_changed.CurrentCount == 0)
                _changed.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    private async Task DeliverAsync(MarshalDbContext db, Notification row, NotificationRouting routing, UserPreferencesRecord owner, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (row.Priority == NotificationPriority.Critical)
        {
            await DeliverCriticalAsync(row, routing, owner, text, now, cancellationToken);
            return;
        }

        if (routing.Channel == DeliveryChannel.Push)
        {
            await PushAsync(row, routing, owner, text, "requested", silent: false, now, cancellationToken);
            return;
        }

        (string? satellite, string reason) = await ChooseSatelliteAsync(row, routing, owner, now, cancellationToken);
        if (satellite is not null)
        {
            if (await HadVoiceSinceAsync(db, row.Source, now - options.Value.SourceVoiceInterval, cancellationToken))
            {
                reason = "source_voice_cap";
            }
            else if (await VoiceCountSinceAsync(db, now.AddHours(-1), cancellationToken) >= options.Value.VoicePerHour)
            {
                reason = "voice_cap";
            }
            else
            {
                NotificationChannelKind kind = routing.FollowUp && Channel(NotificationChannelKind.VoiceConversation) is not null
                    ? NotificationChannelKind.VoiceConversation
                    : NotificationChannelKind.VoiceAnnounce;
                Result spoken = await DeliverOnAsync(kind, new ChannelMessage(row.Id, row.OwnerId, text, row.Priority, Satellite: satellite, TaskId: row.TaskId, PendingActionId: routing.PendingActionId), cancellationToken);
                if (spoken.IsSuccess)
                {
                    Finish(row, routing, "voice", reason, now);
                    return;
                }

                reason = "voice_failed";
            }
        }

        await PushAsync(row, routing, owner, text, reason, silent: false, now, cancellationToken);
    }

    // Voice DND, then an explicit target, then presence.
    private async Task<(string? Satellite, string Reason)> ChooseSatelliteAsync(Notification row, NotificationRouting routing, UserPreferencesRecord owner, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (owner.DndUntil > now)
            return (null, "dnd");
        if (routing.Target is { } target)
            return (target, "requested");

        Presence where = await presence.GetAsync(row.OwnerId, cancellationToken);
        return where switch
        {
            { State: PresenceState.Home, Satellite: { } satellite } => (satellite, "presence"),
            { State: PresenceState.Home } => (null, "no_room"),
            { State: PresenceState.Away } => (null, "away"),
            _ => (null, "presence_unknown"),
        };
    }

    private async Task DeliverCriticalAsync(Notification row, NotificationRouting routing, UserPreferencesRecord owner, string text, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var channels = new List<string>();
        string? satellite = routing.Target ?? (await presence.GetAsync(row.OwnerId, cancellationToken)).Satellite;
        if (satellite is not null && (await DeliverOnAsync(NotificationChannelKind.VoiceAnnounce, new ChannelMessage(row.Id, row.OwnerId, text, row.Priority, Satellite: satellite), cancellationToken)).IsSuccess)
            channels.Add("voice");
        if (owner.NotificationTargets.Push.Count > 0 && (await DeliverOnAsync(NotificationChannelKind.Push, PushMessage(row, routing, text, silent: false), cancellationToken)).IsSuccess)
            channels.Add("push");

        if (channels.Count == 0)
            Inbox(row, "critical_undeliverable");
        else
            Finish(row, routing, string.Join('+', channels), "critical", now);
    }

    private async Task PushAsync(Notification row, NotificationRouting routing, UserPreferencesRecord owner, string text, string reason, bool silent, DateTimeOffset now, CancellationToken cancellationToken)
    {
        if (owner.NotificationTargets.Push.Count == 0)
        {
            Inbox(row, "no_push_target");
            return;
        }

        Result pushed = await DeliverOnAsync(NotificationChannelKind.Push, PushMessage(row, routing, text, silent), cancellationToken);
        if (pushed.IsSuccess)
            Finish(row, routing, "push", reason, now);
        else
            Inbox(row, "push_failed");
    }

    private ChannelMessage PushMessage(Notification row, NotificationRouting routing, string text, bool silent) => new(
        row.Id,
        row.OwnerId,
        text,
        row.Priority,
        Silent: silent,
        Link: routing.Link is { } link && options.Value.PublicUrl is { } baseUrl ? new Uri(baseUrl, link).ToString() : null,
        Tag: routing.Tag,
        Actions: routing.Actions);

    private Task<Result> DeliverOnAsync(NotificationChannelKind kind, ChannelMessage message, CancellationToken cancellationToken) =>
        Channel(kind) is { } channel
            ? channel.DeliverAsync(message, cancellationToken)
            : Task.FromResult(Result.Failure(new Error("notifications.channel_missing", $"No {kind} channel is configured.", ErrorType.Unavailable)));

    private INotificationChannel? Channel(NotificationChannelKind kind) => _channels.FirstOrDefault(channel => channel.Kind == kind);

    private DateTimeOffset? QuietUntil(UserPreferencesRecord owner, NotificationRouting routing, DateTimeOffset now)
    {
        if (routing.OverrideQuietHoursUntil > now)
            return null;
        TimeZoneInfo zone = TimeZoneInfo.TryFindSystemTimeZoneById(owner.TimeZone, out TimeZoneInfo? found) ? found : TimeZoneInfo.Utc;
        return QuietHours.ActiveUntil(owner.QuietHours, now, zone);
    }

    private static async Task<Notification?> OpenWindowAsync(MarshalDbContext db, NotificationRouting routing, DateTimeOffset now, CancellationToken cancellationToken)
    {
        List<Notification> open = await db.Notifications
            .Where(row => row.Outcome == NotificationOutcome.Pending && row.DeliverAfter > now && row.Priority == NotificationPriority.Normal)
            .OrderBy(row => row.CreatedAt)
            .ToListAsync(cancellationToken);
        return open.FirstOrDefault(row => TaskJson.Deserialize<NotificationRouting>(row.Routing) is { Coalescable: true } other
            && other.Channel == routing.Channel && other.Target == routing.Target);
    }

    private static Task<bool> HadVoiceSinceAsync(MarshalDbContext db, string source, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.Notifications.AnyAsync(row => row.Source == source && row.Channel != null && row.Channel.StartsWith("voice") && row.DeliveredAt >= since, cancellationToken);

    private static Task<int> VoiceCountSinceAsync(MarshalDbContext db, DateTimeOffset since, CancellationToken cancellationToken) =>
        db.Notifications.CountAsync(row => row.Channel != null && row.Channel.StartsWith("voice") && row.DeliveredAt >= since, cancellationToken);

    private static void Finish(Notification row, NotificationRouting routing, string channel, string reason, DateTimeOffset now)
    {
        row.Outcome = routing.IsFallback ? NotificationOutcome.Fallback : NotificationOutcome.Delivered;
        row.Channel = channel;
        row.Reason = reason;
        row.DeliveredAt = now;
        row.DeliverAfter = null;
    }

    private static void Inbox(Notification row, string reason)
    {
        row.Outcome = NotificationOutcome.Inboxed;
        row.Channel = null;
        row.Reason = reason;
        row.DeliverAfter = null;
    }

    private static Error Invalid(string message) => new("notifications.invalid", message, ErrorType.Validation);
}

/// <summary>What a notification asked for, stored with it for delivering later.</summary>
internal sealed record NotificationRouting(
    DeliveryChannel? Channel,
    string? Target,
    bool FollowUp,
    string? Link,
    string? Tag,
    IReadOnlyList<NotificationAction> Actions,
    DateTimeOffset? OverrideQuietHoursUntil,
    bool IsFallback,
    PendingActionId? PendingActionId = null)
{
    /// <summary>Actions and follow-ups need their own message.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool Coalescable => Actions.Count == 0 && !FollowUp;

    public static NotificationRouting From(NotificationRequest request) => new(
        request.Channel == DeliveryChannel.Voice && request.Target is null && !request.FollowUp ? null : request.Channel,
        request.Target,
        request.FollowUp,
        request.Link,
        request.Tag,
        request.Actions,
        request.OverrideQuietHoursUntil,
        request.IsFallback,
        request.PendingActionId);
}
