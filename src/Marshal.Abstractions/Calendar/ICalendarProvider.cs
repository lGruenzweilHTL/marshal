namespace Marshal.Abstractions.Calendar;

/// <summary>
/// Calendars and their events (Home Assistant <c>calendar.*</c> entities in v1). Reading, creating and updating
/// only: there is no delete.
/// </summary>
public interface ICalendarProvider
{
    /// <summary>False when no calendar source is configured; the calendar tools are hidden then.</summary>
    bool IsConfigured { get; }

    /// <summary>The calendars with what each supports; empty when none exist.</summary>
    Task<Result<IReadOnlyList<CalendarInfo>>> ListCalendarsAsync(CancellationToken cancellationToken);

    /// <summary>Events overlapping [<paramref name="start"/>, <paramref name="end"/>), times in <paramref name="zone"/>.</summary>
    Task<Result<IReadOnlyList<CalendarEvent>>> ListEventsAsync(string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken cancellationToken);

    Task<Result> CreateEventAsync(string calendarId, CalendarEventData data, CancellationToken cancellationToken);

    /// <summary>Replaces an event's fields; <paramref name="recurrenceId"/> picks one instance of a recurring event.</summary>
    Task<Result> UpdateEventAsync(string calendarId, string eventId, string? recurrenceId, CalendarEventData data, CancellationToken cancellationToken);
}

/// <summary>What a calendar supports.</summary>
[Flags]
public enum CalendarCapabilities
{
    None = 0,
    Create = 1,
    Update = 2,
}

/// <summary>A calendar.</summary>
/// <param name="Id">Calendar id (e.g. <c>calendar.personal</c>).</param>
/// <param name="Name">Display name.</param>
/// <param name="Capabilities">Supported changes.</param>
public sealed record CalendarInfo(string Id, string Name, CalendarCapabilities Capabilities);

/// <summary>A point in time, or a whole day for all-day events.</summary>
/// <param name="DateTime">Start or end time; null for all-day events.</param>
/// <param name="Date">Date of an all-day event (the end date is exclusive); null for timed events.</param>
public sealed record CalendarTime(DateTimeOffset? DateTime, DateOnly? Date)
{
    public static CalendarTime At(DateTimeOffset time) => new(time, null);

    public static CalendarTime AllDay(DateOnly date) => new(null, date);
}

/// <summary>An event.</summary>
/// <param name="CalendarId">The calendar it belongs to.</param>
/// <param name="Id">Event id (null when the calendar has no ids; such events cannot be updated).</param>
/// <param name="RecurrenceId">Instance of a recurring event, or null.</param>
/// <param name="Summary">Title.</param>
/// <param name="Start">Start.</param>
/// <param name="End">End.</param>
/// <param name="Description">Description.</param>
/// <param name="Location">Location.</param>
public sealed record CalendarEvent(string CalendarId, string? Id, string? RecurrenceId, string Summary, CalendarTime Start, CalendarTime End, string? Description, string? Location);

/// <summary>The fields of a new or updated event.</summary>
public sealed record CalendarEventData(string Summary, CalendarTime Start, CalendarTime End, string? Description = null, string? Location = null);
