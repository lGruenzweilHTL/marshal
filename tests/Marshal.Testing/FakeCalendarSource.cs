using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;

namespace Marshal.Testing;

/// <summary>
/// An in-memory calendar source owning the ids that start with its prefix. Records queries, creations and updates;
/// <see cref="Failure"/> makes every call fail.
/// </summary>
public sealed class FakeCalendarSource(string prefix = "calendar.") : ICalendarSource
{
    public bool IsConfigured { get; set; } = true;

    /// <summary>Replaces the overview computed from <see cref="Calendars"/>.</summary>
    public CalendarOverview? OverviewOverride { get; set; }

    public List<CalendarInfo> Calendars { get; } = [];

    /// <summary>Events per calendar id; <see cref="ListEventsAsync"/> returns those overlapping the range.</summary>
    public Dictionary<string, List<CalendarEvent>> Events { get; } = [];

    public Error? Failure { get; set; }

    public List<(string Calendar, DateTimeOffset Start, DateTimeOffset End, TimeZoneInfo Zone)> EventQueries { get; } = [];

    public List<(string Calendar, CalendarEventData Data)> Created { get; } = [];

    public List<(string Calendar, string Id, string? RecurrenceId, CalendarEventData Data, CalendarUpdateScope? Scope)> Updated { get; } = [];

    public CalendarOverview Overview =>
        OverviewOverride ?? new CalendarOverview(Calendars.Count, Calendars.Aggregate(CalendarCapabilities.None, (all, calendar) => all | calendar.Capabilities));

    public bool Owns(string calendarId) => calendarId.StartsWith(prefix, StringComparison.Ordinal);

    public FakeCalendarSource WithCalendar(string id, string name, CalendarCapabilities capabilities, params CalendarEvent[] events)
    {
        Calendars.Add(new CalendarInfo(id, name, capabilities));
        Events[id] = [.. events];
        return this;
    }

    public Task<Result<IReadOnlyList<CalendarInfo>>> ListCalendarsAsync(CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } failure ? failure : Result.Success<IReadOnlyList<CalendarInfo>>([.. Calendars]));

    public Task<Result<IReadOnlyList<CalendarEvent>>> ListEventsAsync(string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        EventQueries.Add((calendarId, start, end, zone));
        if (Failure is { } failure)
            return Task.FromResult<Result<IReadOnlyList<CalendarEvent>>>(failure);
        if (!Events.TryGetValue(calendarId, out List<CalendarEvent>? events))
            return Task.FromResult<Result<IReadOnlyList<CalendarEvent>>>(CalendarErrors.NotFound(calendarId));

        return Task.FromResult<Result<IReadOnlyList<CalendarEvent>>>(events.Where(item => Instant(item.Start, zone) < end && Instant(item.End, zone) > start).ToList());
    }

    public Task<Result> CreateEventAsync(string calendarId, CalendarEventData data, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
            return Task.FromResult<Result>(failure);
        Created.Add((calendarId, data));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> UpdateEventAsync(string calendarId, string eventId, string? recurrenceId, CalendarEventData data, CalendarUpdateScope? scope, CancellationToken cancellationToken)
    {
        if (Failure is { } failure)
            return Task.FromResult<Result>(failure);
        Updated.Add((calendarId, eventId, recurrenceId, data, scope));
        return Task.FromResult(Result.Success());
    }

    private static DateTimeOffset Instant(CalendarTime time, TimeZoneInfo zone)
    {
        if (time.DateTime is { } at)
            return at;

        var local = time.Date!.Value.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }
}
