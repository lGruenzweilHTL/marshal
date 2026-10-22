namespace Marshal.Abstractions.Calendar;

/// <summary>
/// Calendars and their events: Home Assistant <c>calendar.*</c> entities and CalDAV accounts, combined into one by a
/// composite over the <see cref="ICalendarSource"/>s. Reading, creating and updating only: there is no delete.
/// </summary>
public interface ICalendarProvider
{
    /// <summary>False when no calendar source is configured; the calendar tools are hidden then.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// How many calendars exist and what they support together, from cached state: cheap enough for the visibility
    /// check before every model call.
    /// </summary>
    CalendarOverview Overview { get; }

    /// <summary>The calendars with what each supports; empty when none exist.</summary>
    Task<Result<IReadOnlyList<CalendarInfo>>> ListCalendarsAsync(CancellationToken cancellationToken);

    /// <summary>Events overlapping [<paramref name="start"/>, <paramref name="end"/>), times in <paramref name="zone"/>.</summary>
    Task<Result<IReadOnlyList<CalendarEvent>>> ListEventsAsync(string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken cancellationToken);

    Task<Result> CreateEventAsync(string calendarId, CalendarEventData data, CancellationToken cancellationToken);

    /// <summary>
    /// Replaces an event's fields; <paramref name="recurrenceId"/> picks one instance of a recurring event.
    /// <paramref name="scope"/> chooses between that instance and the whole series; only calendars with
    /// <see cref="CalendarCapabilities.UpdateOccurrence"/> accept it, others take null.
    /// </summary>
    Task<Result> UpdateEventAsync(string calendarId, string eventId, string? recurrenceId, CalendarEventData data, CalendarUpdateScope? scope, CancellationToken cancellationToken);
}

/// <summary>One calendar provider behind the composite; it owns the calendar ids it recognizes.</summary>
public interface ICalendarSource : ICalendarProvider
{
    /// <summary>True when <paramref name="calendarId"/> belongs to this source (by its id format, without a request).</summary>
    bool Owns(string calendarId);
}

/// <summary>What an update of a recurring event changes.</summary>
public enum CalendarUpdateScope
{
    /// <summary>Only the instance named by its recurrence id.</summary>
    Occurrence,

    /// <summary>The whole series.</summary>
    Series,
}

/// <summary>What a calendar supports.</summary>
[Flags]
public enum CalendarCapabilities
{
    None = 0,
    Create = 1,
    Update = 2,

    /// <summary>Single instances of a recurring event can be changed (<see cref="CalendarUpdateScope"/>).</summary>
    UpdateOccurrence = 4,

    /// <summary>Events carry their organizer and attendees.</summary>
    Attendees = 8,
}

/// <summary>The calendars at a glance.</summary>
/// <param name="Count">Number of calendars.</param>
/// <param name="Capabilities">Changes at least one calendar supports.</param>
public sealed record CalendarOverview(int Count, CalendarCapabilities Capabilities);

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

/// <summary>
/// An event. The fields after the constructor come only from providers that have them (CalDAV); others leave them at
/// their defaults.
/// </summary>
/// <param name="CalendarId">The calendar it belongs to.</param>
/// <param name="Id">Event id (null when the calendar has no ids; such events cannot be updated).</param>
/// <param name="RecurrenceId">Instance of a recurring event, or null.</param>
/// <param name="Summary">Title.</param>
/// <param name="Start">Start.</param>
/// <param name="End">End.</param>
/// <param name="Description">Description.</param>
/// <param name="Location">Location.</param>
public sealed record CalendarEvent(string CalendarId, string? Id, string? RecurrenceId, string Summary, CalendarTime Start, CalendarTime End, string? Description, string? Location)
{
    /// <summary>Who organizes it; null when unknown.</summary>
    public CalendarPerson? Organizer { get; init; }

    /// <summary>Invited people with their answers; null when the provider has no attendee data.</summary>
    public IReadOnlyList<CalendarAttendee>? Attendees { get; init; }

    /// <summary>Whether the event belongs to a recurring series.</summary>
    public bool Recurring { get; init; }

    /// <summary>The series an occurrence belongs to; null for single events and providers without series ids.</summary>
    public string? SeriesId { get; init; }

    /// <summary>For an occurrence, its start before any move; null otherwise.</summary>
    public DateTimeOffset? OriginalStart { get; init; }

    /// <summary>Marked as free time (<c>TRANSP:TRANSPARENT</c>): it does not block free-time search.</summary>
    public bool Transparent { get; init; }
}

/// <summary>A person on an event.</summary>
/// <param name="Name">Display name, if given.</param>
/// <param name="Address">Calendar address (usually the e-mail address, without <c>mailto:</c>).</param>
public sealed record CalendarPerson(string? Name, string Address);

/// <summary>An invited person.</summary>
/// <param name="Name">Display name, if given.</param>
/// <param name="Address">Calendar address (usually the e-mail address, without <c>mailto:</c>).</param>
/// <param name="Role">iCalendar role, e.g. <c>REQ-PARTICIPANT</c>, <c>OPT-PARTICIPANT</c>, <c>CHAIR</c>.</param>
/// <param name="Status">Their answer.</param>
/// <param name="IsAccount">Whether this attendee is the calendar's own account (its answer decides free time).</param>
public sealed record CalendarAttendee(string? Name, string Address, string Role, CalendarRsvp Status, bool IsAccount = false);

/// <summary>An attendee's answer (iCalendar <c>PARTSTAT</c>).</summary>
public enum CalendarRsvp
{
    NeedsAction,
    Accepted,
    Declined,
    Tentative,
    Delegated,
}

/// <summary>The fields of a new or updated event.</summary>
public sealed record CalendarEventData(string Summary, CalendarTime Start, CalendarTime End, string? Description = null, string? Location = null);
