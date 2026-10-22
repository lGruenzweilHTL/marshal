using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;

namespace Marshal.Demo.HomeAssistant;

/// <summary>The simulated Home Assistant calendars (family and work); created and updated events stay for the process run.</summary>
public sealed class DemoCalendarProvider(DemoWorld world) : ICalendarSource
{
    private const CalendarCapabilities All = CalendarCapabilities.Create | CalendarCapabilities.Update;

    public bool IsConfigured => true;

    public bool Owns(string calendarId) => calendarId.StartsWith("calendar.", StringComparison.Ordinal);

    public CalendarOverview Overview => new(world.Home.Calendars.Count, All);

    public Task<Result<IReadOnlyList<CalendarInfo>>> ListCalendarsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<CalendarInfo>>>(world.Home.Calendars.Select(calendar => new CalendarInfo(calendar.Id, calendar.Name, All)).ToList());

    public Task<Result<IReadOnlyList<CalendarEvent>>> ListEventsAsync(string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken cancellationToken) =>
        Task.FromResult(world.Home.WithCalendars<Result<IReadOnlyList<CalendarEvent>>>(calendars =>
        {
            if (calendars.FirstOrDefault(calendar => calendar.Id == calendarId) is not { } found)
                return NotFound(calendarId);

            return found.Events
                .Where(item => Instant(item.Start, zone) < end && Instant(item.End, zone) > start)
                .OrderBy(item => Instant(item.Start, zone))
                .Select(item => new CalendarEvent(calendarId, item.Id, null, item.Summary, item.Start, item.End, item.Description, item.Location))
                .ToList();
        }));

    public Task<Result> CreateEventAsync(string calendarId, CalendarEventData data, CancellationToken cancellationToken) =>
        Task.FromResult(world.Home.WithCalendars<Result>(calendars =>
        {
            if (calendars.FirstOrDefault(calendar => calendar.Id == calendarId) is not { } found)
                return NotFound(calendarId);

            found.Events.Add(new DemoCalendarEvent(Guid.NewGuid().ToString("N"), data.Summary, data.Start, data.End, data.Location, data.Description));
            return Result.Success();
        }));

    public Task<Result> UpdateEventAsync(string calendarId, string eventId, string? recurrenceId, CalendarEventData data, CalendarUpdateScope? scope, CancellationToken cancellationToken) =>
        Task.FromResult(world.Home.WithCalendars<Result>(calendars =>
        {
            if (calendars.FirstOrDefault(calendar => calendar.Id == calendarId) is not { } found)
                return NotFound(calendarId);

            int index = found.Events.FindIndex(item => item.Id == eventId);
            if (index < 0)
                return CalendarErrors.EventNotFound(eventId);

            found.Events[index] = new DemoCalendarEvent(eventId, data.Summary, data.Start, data.End, data.Location, data.Description);
            return Result.Success();
        }));

    private static DateTimeOffset Instant(CalendarTime time, TimeZoneInfo zone)
    {
        if (time.DateTime is { } at)
            return at;

        var local = time.Date!.Value.ToDateTime(TimeOnly.MinValue);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    private static Error NotFound(string calendarId) => new("calendar.not_found", $"There is no calendar '{calendarId}'.", ErrorType.NotFound);
}
