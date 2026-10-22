using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.CalDav.Sync;

/// <summary>
/// Publishes <c>calendar.*</c> change events for the CalDAV calendars: every <c>pollMinutes</c> it asks each calendar
/// what changed since the stored sync token (or compares entity tags when the server has no sync support), reads new
/// and changed resources, and publishes <c>event_created</c> (plus <c>invitation</c> when the account is an attendee
/// who has not answered), <c>event_updated</c> and <c>event_removed</c>. Payloads hold the calendar id, event id,
/// start and end only, so they are trusted; an event uid that is not a plain identifier is left out. The first poll of a
/// calendar and a resync after an expired token only record the state. Accounts belong to the instance owner.
/// </summary>
public sealed partial class CalDavSyncPoller(
    IOptions<CalDavOptions> options,
    CalDavCalendarProvider provider,
    ICalDavClient client,
    ICalDavSyncStore store,
    IInstanceOwner owner,
    IExecutionContextAccessor executionContext,
    IEventPublisher publisher,
    TimeProvider time,
    ILogger<CalDavSyncPoller> logger) : BackgroundService, IEventSource
{
    public const string Name = "calendar";

    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(1);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastPoll = new(StringComparer.Ordinal);
    private volatile bool _connected;

    public string Source => Name;

    public bool IsConnected => _connected;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.IsConfigured)
            return;

        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await PollAsync(stoppingToken);
                }
                catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
                {
                    LogPollFailed(exception);
                }

                await Task.Delay(Tick, time, stoppingToken);
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutting down.
        }
    }

    /// <summary>Polls every calendar whose interval has passed.</summary>
    internal async Task PollAsync(CancellationToken cancellationToken)
    {
        OwnerId ownerId = await owner.GetAsync(cancellationToken);
        using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = ownerId, Source = SessionSource.Task, IsInteractive = false });
        bool anyConnected = false;
        foreach ((string accountId, CalDavAccountOptions settings) in options.Value.Accounts)
        {
            CalDavCalendarProvider.AccountState state = await provider.StateAsync(accountId, cancellationToken);
            if (state.Error is not null)
                continue;

            CalDavAccount account = provider.Account(accountId);
            foreach (CalDavCalendarProvider.KnownCalendar calendar in state.Calendars)
            {
                string calendarName = calendar.Info.Id[(calendar.Info.Id.IndexOf('/', StringComparison.Ordinal) + 1)..];
                CalDavCalendarSync? stored = await store.GetAsync(accountId, calendarName, cancellationToken);
                DateTimeOffset? last = _lastPoll.TryGetValue(calendar.Info.Id, out DateTimeOffset seen) ? seen : stored?.LastPoll;
                if (last is { } previous && time.GetUtcNow() - previous < TimeSpan.FromMinutes(settings.PollMinutes))
                {
                    anyConnected = true;
                    continue;
                }

                if (await PollCalendarAsync(account, calendar, calendarName, stored, state.Addresses, ownerId, cancellationToken))
                    anyConnected = true;
            }
        }

        _connected = anyConnected;
    }

    private async Task<bool> PollCalendarAsync(CalDavAccount account, CalDavCalendarProvider.KnownCalendar calendar, string calendarName, CalDavCalendarSync? stored, IReadOnlyList<string> addresses, OwnerId ownerId, CancellationToken cancellationToken)
    {
        Result<Changes> changes = calendar.SupportsSync
            ? await SyncChangesAsync(account, calendar.Url, stored, cancellationToken)
            : await ETagChangesAsync(account, calendar.Url, stored, cancellationToken);
        if (changes.IsFailure)
        {
            LogCalendarFailed(calendar.Info.Id, changes.Error.Message);
            return false;
        }

        Dictionary<Uri, CalDavSeenResource> resources = stored is null ? [] : new(stored.Resources);
        bool publish = stored is not null && !changes.Value.Resync;
        if (!publish)
            resources.Clear();

        foreach ((Uri href, string? etag) in changes.Value.Changed)
        {
            bool known = resources.ContainsKey(href);
            if (!publish)
            {
                resources[href] = new CalDavSeenResource(etag, null);
                continue;
            }

            Result<CalDavResource> resource = await client.GetAsync(account, href, cancellationToken);
            if (resource.IsFailure || IcsEvents.Load(resource.Value.Data) is not { } ics || IcsEvents.Summary(ics) is not { } summary)
            {
                resources[href] = new CalDavSeenResource(etag, null);
                continue;
            }

            resources[href] = new CalDavSeenResource(resource.Value.ETag ?? etag, summary.Uid);
            Publish(known ? "event_updated" : "event_created", calendar.Info.Id, summary.Uid, summary.Start, summary.End, ownerId);
            if (!known && IcsEvents.IsInvitation(ics, addresses))
                Publish("invitation", calendar.Info.Id, summary.Uid, summary.Start, summary.End, ownerId);
        }

        foreach (Uri href in changes.Value.Removed)
        {
            if (!resources.Remove(href, out CalDavSeenResource? seen) || !publish)
                continue;
            // Resources from before the first poll were never read; servers usually name them after the uid.
            string uid = seen.Uid ?? Path.GetFileNameWithoutExtension(Uri.UnescapeDataString(href.AbsolutePath.TrimEnd('/')));
            Publish("event_removed", calendar.Info.Id, uid, null, null, ownerId);
        }

        DateTimeOffset now = time.GetUtcNow();
        await store.SaveAsync(new CalDavCalendarSync(account.Id, calendarName, changes.Value.Token, resources, now), cancellationToken);
        _lastPoll[calendar.Info.Id] = now;
        return true;
    }

    private async Task<Result<Changes>> SyncChangesAsync(CalDavAccount account, Uri calendar, CalDavCalendarSync? stored, CancellationToken cancellationToken)
    {
        bool resync = false;
        Result<CalDavSyncResult> synced = await client.SyncAsync(account, calendar, stored?.Token, cancellationToken);
        if (synced.IsFailure && synced.Error.Code == CalDavErrors.InvalidSyncToken.Code)
        {
            // The server forgot the token: start over without publishing, as on the first poll.
            resync = true;
            synced = await client.SyncAsync(account, calendar, null, cancellationToken);
        }

        if (synced.IsFailure)
            return synced.Error;

        return new Changes(
            synced.Value.Token,
            [.. synced.Value.Changes.Where(change => !change.Removed).Select(change => (change.Href, change.ETag))],
            [.. synced.Value.Changes.Where(change => change.Removed).Select(change => change.Href)],
            resync);
    }

    private async Task<Result<Changes>> ETagChangesAsync(CalDavAccount account, Uri calendar, CalDavCalendarSync? stored, CancellationToken cancellationToken)
    {
        Result<IReadOnlyDictionary<Uri, string>> etags = await client.ListETagsAsync(account, calendar, cancellationToken);
        if (etags.IsFailure)
            return etags.Error;

        IReadOnlyDictionary<Uri, CalDavSeenResource> before = stored?.Resources ?? new Dictionary<Uri, CalDavSeenResource>();
        return new Changes(
            null,
            [.. etags.Value.Where(entry => !before.TryGetValue(entry.Key, out CalDavSeenResource? seen) || seen.ETag != entry.Value).Select(entry => (entry.Key, (string?)entry.Value))],
            [.. before.Keys.Where(href => !etags.Value.ContainsKey(href))],
            Resync: false);
    }

    private void Publish(string type, string calendarId, string uid, DateTimeOffset? start, DateTimeOffset? end, OwnerId ownerId)
    {
        JsonElement payload = JsonSerializer.SerializeToElement(new
        {
            calendar = calendarId,
            eventId = SafeUid().IsMatch(uid) ? uid : null,
            start,
            end,
        });
        publisher.Publish(new MarshalEvent(Name, type, calendarId, payload, ownerId, time.GetUtcNow(), Trusted: true));
    }

    // Uids are chosen by whoever wrote the event; only plain identifiers go into a trusted payload.
    [GeneratedRegex("^[A-Za-z0-9@._:+-]{1,200}$")]
    private static partial Regex SafeUid();

    [LoggerMessage(Level = LogLevel.Warning, Message = "CalDAV change poll failed")]
    private partial void LogPollFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CalDAV calendar {Calendar} could not be polled: {Reason}")]
    private partial void LogCalendarFailed(string calendar, string reason);

    private sealed record Changes(string? Token, IReadOnlyList<(Uri Href, string? ETag)> Changed, IReadOnlyList<Uri> Removed, bool Resync);
}
