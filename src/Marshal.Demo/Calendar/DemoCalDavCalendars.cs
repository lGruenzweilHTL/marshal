using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;

namespace Marshal.Demo.Calendar;

/// <summary>
/// A simulated CalDAV account (<c>caldav:demo/work</c>) for demo mode without <c>calendar.caldav</c>: a weekly team
/// standup with organizer and attendees, a review the user declined, occurrence and series edits, and an invitation
/// that <see cref="DemoCalDavEventSource"/> delivers shortly after start. Changes stay for the process run.
/// </summary>
public sealed class DemoCalDavCalendars : ICalendarSource
{
    public const string Work = "caldav:demo/work";

    /// <summary>The demo user's calendar address.</summary>
    public const string Account = "you@demo.invalid";

    private const CalendarCapabilities All = CalendarCapabilities.Create | CalendarCapabilities.Update | CalendarCapabilities.UpdateOccurrence | CalendarCapabilities.Attendees;
    private const string Standup = "team-standup";
    private const int StandupWeeks = 10;

    private static readonly CalendarPerson Anna = new("Anna Huber", "anna@demo.invalid");

    private readonly Lock _gate = new();
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
    private readonly List<CalendarEvent> _events = [];

    public DemoCalDavCalendars(DemoWorld world)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(world.Started, _zone).DateTime);
        DateOnly monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        for (int week = 0; week < StandupWeeks; week++)
        {
            DateTimeOffset start = At(monday.AddDays(7 * week), new TimeOnly(9, 0));
            _events.Add(new CalendarEvent(Work, Standup, RecurrenceId(start), "Team standup", CalendarTime.At(start), CalendarTime.At(start.AddMinutes(15)), null, "Room 2")
            {
                Organizer = Anna,
                Attendees =
                [
                    new CalendarAttendee(Anna.Name, Anna.Address, "CHAIR", CalendarRsvp.Accepted),
                    new CalendarAttendee("Ben Novak", "ben@demo.invalid", "REQ-PARTICIPANT", CalendarRsvp.Tentative),
                    new CalendarAttendee("You", Account, "REQ-PARTICIPANT", CalendarRsvp.Accepted, IsAccount: true),
                ],
                Recurring = true,
                SeriesId = Standup,
                OriginalStart = start,
            });
        }

        DateTimeOffset review = At(today.AddDays(1), new TimeOnly(14, 0));
        _events.Add(new CalendarEvent(Work, "quarterly-review", null, "Quarterly review", CalendarTime.At(review), CalendarTime.At(review.AddHours(1)), null, "Video call")
        {
            Organizer = new CalendarPerson("Ben Novak", "ben@demo.invalid"),
            Attendees =
            [
                new CalendarAttendee("Ben Novak", "ben@demo.invalid", "CHAIR", CalendarRsvp.Accepted),
                new CalendarAttendee("You", Account, "OPT-PARTICIPANT", CalendarRsvp.Declined, IsAccount: true),
            ],
        });
    }

    public bool IsConfigured => true;

    public bool Owns(string calendarId) => calendarId.StartsWith("caldav:", StringComparison.Ordinal);

    public CalendarOverview Overview => new(1, All);

    public Task<Result<IReadOnlyList<CalendarInfo>>> ListCalendarsAsync(CancellationToken cancellationToken) =>
        Task.FromResult<Result<IReadOnlyList<CalendarInfo>>>(new List<CalendarInfo> { new(Work, "Work", All) });

    public Task<Result<IReadOnlyList<CalendarEvent>>> ListEventsAsync(string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken cancellationToken)
    {
        if (calendarId != Work)
            return Task.FromResult<Result<IReadOnlyList<CalendarEvent>>>(CalendarErrors.NotFound(calendarId));

        lock (_gate)
        {
            return Task.FromResult<Result<IReadOnlyList<CalendarEvent>>>(_events
                .Where(item => item.Start.DateTime < end && item.End.DateTime > start)
                .OrderBy(item => item.Start.DateTime)
                .Select(item => item with { Start = CalendarTime.At(TimeZoneInfo.ConvertTime(item.Start.DateTime!.Value, zone)), End = CalendarTime.At(TimeZoneInfo.ConvertTime(item.End.DateTime!.Value, zone)) })
                .ToList());
        }
    }

    public Task<Result> CreateEventAsync(string calendarId, CalendarEventData data, CancellationToken cancellationToken)
    {
        if (calendarId != Work)
            return Task.FromResult<Result>(CalendarErrors.NotFound(calendarId));
        if (data.Start.DateTime is null)
            return Task.FromResult<Result>(CalendarErrors.Invalid("The demo work calendar holds timed events only."));

        Add(new CalendarEvent(Work, Guid.NewGuid().ToString("N"), null, data.Summary, data.Start, data.End, data.Description, data.Location));
        return Task.FromResult(Result.Success());
    }

    public Task<Result> UpdateEventAsync(string calendarId, string eventId, string? recurrenceId, CalendarEventData data, CalendarUpdateScope? scope, CancellationToken cancellationToken)
    {
        if (calendarId != Work)
            return Task.FromResult<Result>(CalendarErrors.NotFound(calendarId));
        if (data.Start.DateTime is not { } start || data.End.DateTime is not { } end)
            return Task.FromResult<Result>(CalendarErrors.Invalid("The demo work calendar holds timed events only."));

        lock (_gate)
        {
            List<int> matching = [.. _events.Select((item, index) => (item, index)).Where(pair => pair.item.Id == eventId).Select(pair => pair.index)];
            if (matching.Count == 0)
                return Task.FromResult<Result>(CalendarErrors.EventNotFound(eventId));

            bool series = _events[matching[0]].Recurring;
            CalendarUpdateScope effective = scope ?? (recurrenceId is not null && series ? CalendarUpdateScope.Occurrence : CalendarUpdateScope.Series);
            if (effective == CalendarUpdateScope.Occurrence)
            {
                if (!series || matching.FirstOrDefault(index => _events[index].RecurrenceId == recurrenceId, -1) is not (>= 0 and var one))
                    return Task.FromResult<Result>(CalendarErrors.Invalid("scope occurrence needs the recurrenceId of an occurrence from calendar_list_events."));
                _events[one] = Changed(_events[one], data, start, end - start);
                return Task.FromResult(Result.Success());
            }

            // A series edit moves every occurrence by the move of the named one (or of the first).
            CalendarEvent named = _events[matching.FirstOrDefault(index => _events[index].RecurrenceId == recurrenceId, matching[0])];
            TimeSpan shift = start - named.Start.DateTime!.Value;
            foreach (int index in matching)
                _events[index] = Changed(_events[index], data, _events[index].Start.DateTime!.Value + shift, end - start);
            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>Adds an event (the simulated server receiving it).</summary>
    internal void Add(CalendarEvent item)
    {
        lock (_gate)
            _events.Add(item);
    }

    private static CalendarEvent Changed(CalendarEvent item, CalendarEventData data, DateTimeOffset start, TimeSpan duration) =>
        item with { Summary = data.Summary, Start = CalendarTime.At(start), End = CalendarTime.At(start + duration), Description = data.Description, Location = data.Location };

    internal DateTimeOffset At(DateOnly day, TimeOnly time)
    {
        var local = day.ToDateTime(time);
        return new DateTimeOffset(local, _zone.GetUtcOffset(local));
    }

    private static string RecurrenceId(DateTimeOffset start) => start.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
}
