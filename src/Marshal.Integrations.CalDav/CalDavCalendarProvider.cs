using System.Collections.Concurrent;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Health;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.CalDav;

/// <summary>
/// The calendars of the configured CalDAV accounts, with ids <c>caldav:{account}/{calendar}</c>. Discovery is cached per
/// account for an hour; a failed account is left out (with a health warning) and retried after five minutes, so one
/// unreachable server never hides the others.
/// </summary>
public sealed partial class CalDavCalendarProvider(IOptions<CalDavOptions> options, ICalDavClient client, TimeProvider time, ILogger<CalDavCalendarProvider> logger)
    : ICalendarSource, IHealthWarningProvider
{
    private const string Prefix = "caldav:";
    private static readonly TimeSpan Fresh = TimeSpan.FromHours(1);
    private static readonly TimeSpan Retry = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, AccountState> _states = new(StringComparer.Ordinal);

    public bool IsConfigured => options.Value.IsConfigured;

    public CalendarOverview Overview
    {
        get
        {
            List<CalendarInfo> calendars = [.. _states.Values.SelectMany(state => state.Calendars).Select(calendar => calendar.Info)];
            return new CalendarOverview(calendars.Count, calendars.Aggregate(CalendarCapabilities.None, (all, calendar) => all | calendar.Capabilities));
        }
    }

    public bool Owns(string calendarId) => calendarId.StartsWith(Prefix, StringComparison.Ordinal);

    public async Task<Result<IReadOnlyList<CalendarInfo>>> ListCalendarsAsync(CancellationToken cancellationToken)
    {
        List<CalendarInfo> calendars = [];
        Error? last = null;
        int failed = 0;
        foreach (string account in options.Value.Accounts.Keys)
        {
            AccountState state = await StateAsync(account, cancellationToken);
            if (state.Error is { } error)
            {
                failed++;
                last = error;
                continue;
            }

            calendars.AddRange(state.Calendars.Select(calendar => calendar.Info));
        }

        if (failed > 0 && failed == options.Value.Accounts.Count)
            return CalendarErrors.Failed($"No CalDAV account answered: {last!.Message}");
        return calendars;
    }

    public async Task<Result<IReadOnlyList<CalendarEvent>>> ListEventsAsync(string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        Result<Located> located = await LocateAsync(calendarId, cancellationToken);
        if (located.IsFailure)
            return located.Error;

        (CalDavAccount account, AccountState state, KnownCalendar calendar) = located.Value;
        // Series that started before the range still have occurrences in it; servers expand the query by RRULE.
        Result<IReadOnlyList<CalDavResource>> resources = await client.QueryAsync(account, calendar.Url, start, end, cancellationToken);
        if (resources.IsFailure)
            return CalendarErrors.Failed($"Calendar '{calendarId}' could not be read: {resources.Error.Message}");

        List<CalendarEvent> events = [];
        foreach (CalDavResource resource in resources.Value)
        {
            if (IcsEvents.Load(resource.Data) is not { } ics)
            {
                LogUnreadable(resource.Href);
                continue;
            }

            events.AddRange(IcsEvents.Occurrences(ics, calendarId, start, end, zone, state.Addresses));
        }

        return events.OrderBy(entry => entry.Start.DateTime ?? new DateTimeOffset(entry.Start.Date!.Value.ToDateTime(TimeOnly.MinValue), zone.GetUtcOffset(entry.Start.Date!.Value.ToDateTime(TimeOnly.MinValue)))).ToList();
    }

    public async Task<Result> CreateEventAsync(string calendarId, CalendarEventData data, CancellationToken cancellationToken)
    {
        Result<Located> located = await LocateAsync(calendarId, cancellationToken);
        if (located.IsFailure)
            return located.Error;

        (CalDavAccount account, _, KnownCalendar calendar) = located.Value;
        if (!calendar.Info.Capabilities.HasFlag(CalendarCapabilities.Create))
            return CalendarErrors.Unsupported(calendarId, "creating");

        string uid = $"{Guid.NewGuid():N}@marshal";
        var resource = new Uri(calendar.Url, $"{Guid.NewGuid():N}.ics");
        Result<string?> put = await client.PutAsync(account, resource, IcsEvents.Create(uid, data, time.GetUtcNow()), null, cancellationToken);
        return put.IsFailure ? CalendarErrors.Failed($"The event could not be created: {put.Error.Message}") : Result.Success();
    }

    public async Task<Result> UpdateEventAsync(string calendarId, string eventId, string? recurrenceId, CalendarEventData data, CalendarUpdateScope? scope, CancellationToken cancellationToken)
    {
        Result<Located> located = await LocateAsync(calendarId, cancellationToken);
        if (located.IsFailure)
            return located.Error;

        (CalDavAccount account, _, KnownCalendar calendar) = located.Value;
        if (!calendar.Info.Capabilities.HasFlag(CalendarCapabilities.Update))
            return CalendarErrors.Unsupported(calendarId, "updating");

        Result<CalDavResource?> found = await client.FindByUidAsync(account, calendar.Url, eventId, cancellationToken);
        if (found.IsFailure)
            return CalendarErrors.Failed($"Calendar '{calendarId}' could not be read: {found.Error.Message}");
        if (found.Value is not { } resource)
            return CalendarErrors.EventNotFound(eventId);
        if (IcsEvents.Load(resource.Data) is not { } ics)
            return CalendarErrors.Failed($"Event '{eventId}' could not be read.");

        Result<string> updated = IcsEvents.Update(ics, calendarId, eventId, recurrenceId, data, scope, time.GetUtcNow());
        if (updated.IsFailure)
            return updated.Error;

        Result<string?> put = await client.PutAsync(account, resource.Href, updated.Value, resource.ETag, cancellationToken);
        if (put.IsFailure)
        {
            return put.Error.Code == CalDavErrors.Conflict.Code
                ? CalendarErrors.Failed($"Event '{eventId}' changed on the server meanwhile; list the events again and retry.")
                : CalendarErrors.Failed($"The event could not be updated: {put.Error.Message}");
        }

        return Result.Success();
    }

    public async ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken)
    {
        List<HealthCondition> conditions = [];
        foreach ((string account, AccountState state) in _states.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            if (state.Error is not { } error || !options.Value.Accounts.ContainsKey(account))
                continue;

            HealthText message = error.Code == "caldav.auth_failed"
                ? new HealthText($"Das CalDAV-Konto '{account}' lehnt die Anmeldung ab. Benutzername und Passwort-Secret prüfen.", $"CalDAV account '{account}' rejects the login. Check the username and password secret.")
                : new HealthText($"Das CalDAV-Konto '{account}' ist nicht erreichbar; seine Kalender fehlen.", $"CalDAV account '{account}' is unreachable; its calendars are missing.");
            conditions.Add(new HealthCondition($"caldav:{account}", error.Code, HealthSeverity.Warning, message, "/system/health"));
        }

        await Task.CompletedTask;
        return conditions;
    }

    /// <summary>The account settings for the client.</summary>
    internal CalDavAccount Account(string id)
    {
        CalDavAccountOptions settings = options.Value.Accounts[id];
        return new CalDavAccount(id, settings.Url!, settings.Username!, settings.PasswordSecret!);
    }

    /// <summary>The account's calendars and addresses, from the cache while fresh.</summary>
    internal async Task<AccountState> StateAsync(string account, CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        if (_states.TryGetValue(account, out AccountState? cached) && now - cached.At < (cached.Error is null ? Fresh : Retry))
            return cached;

        Result<CalDavDiscovery> discovery = await client.DiscoverAsync(Account(account), cancellationToken);
        AccountState state;
        if (discovery.IsFailure)
        {
            LogDiscoveryFailed(account, discovery.Error.Message);
            state = new AccountState([], [], discovery.Error, now);
        }
        else
        {
            state = new AccountState(Calendars(account, discovery.Value), Addresses(account, discovery.Value), null, now);
        }

        _states[account] = state;
        return state;
    }

    private List<KnownCalendar> Calendars(string account, CalDavDiscovery discovery)
    {
        List<string>? names = options.Value.Accounts[account].Calendars;
        return
        [
            .. discovery.Calendars
                .Where(calendar => names is null || names.Contains(calendar.Name, StringComparer.Ordinal))
                .Select(calendar => new KnownCalendar(
                    calendar.Url,
                    new CalendarInfo(
                        $"{Prefix}{account}/{calendar.Name}",
                        calendar.DisplayName ?? calendar.Name,
                        calendar.CanWrite
                            ? CalendarCapabilities.Create | CalendarCapabilities.Update | CalendarCapabilities.UpdateOccurrence | CalendarCapabilities.Attendees
                            : CalendarCapabilities.Attendees),
                    calendar.SupportsSync)),
        ];
    }

    // The server's mailto addresses; Radicale lists none, so a username that is an address stands in.
    private List<string> Addresses(string account, CalDavDiscovery discovery)
    {
        if (discovery.Addresses.Count > 0)
            return [.. discovery.Addresses];
        string username = options.Value.Accounts[account].Username!;
        return username.Contains('@', StringComparison.Ordinal) ? [username] : [];
    }

    private async Task<Result<Located>> LocateAsync(string calendarId, CancellationToken cancellationToken)
    {
        int slash = Owns(calendarId) ? calendarId.IndexOf('/', Prefix.Length) : -1;
        if (slash < 0)
            return CalendarErrors.NotFound(calendarId);

        string account = calendarId[Prefix.Length..slash];
        if (!options.Value.Accounts.ContainsKey(account))
            return CalendarErrors.NotFound(calendarId);

        AccountState state = await StateAsync(account, cancellationToken);
        if (state.Error is { } error)
            return CalendarErrors.Failed($"CalDAV account '{account}' is unavailable: {error.Message}");

        KnownCalendar? calendar = state.Calendars.FirstOrDefault(entry => entry.Info.Id == calendarId);
        return calendar is null ? CalendarErrors.NotFound(calendarId) : new Located(Account(account), state, calendar);
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "CalDAV discovery for account {Account} failed: {Reason}")]
    private partial void LogDiscoveryFailed(string account, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "CalDAV resource {Href} is not valid iCalendar; skipped")]
    private partial void LogUnreadable(Uri href);

    /// <summary>A discovered calendar.</summary>
    internal sealed record KnownCalendar(Uri Url, CalendarInfo Info, bool SupportsSync);

    /// <summary>What discovery found for an account, or why it failed, and when.</summary>
    internal sealed record AccountState(IReadOnlyList<KnownCalendar> Calendars, IReadOnlyList<string> Addresses, Error? Error, DateTimeOffset At);

    private sealed record Located(CalDavAccount Account, AccountState State, KnownCalendar Calendar);
}
