using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Core.Calendar;
using Marshal.Testing;

namespace Marshal.Core.Tests.Calendar;

public sealed class CompositeCalendarProviderTests
{
    private static readonly CalendarTime Nine = CalendarTime.At(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(2)));
    private static readonly CalendarTime Ten = CalendarTime.At(new DateTimeOffset(2026, 10, 22, 10, 0, 0, TimeSpan.FromHours(2)));

    private readonly FakeCalendarSource _ha = new FakeCalendarSource("calendar.")
        .WithCalendar("calendar.family", "Family", CalendarCapabilities.Create | CalendarCapabilities.Update,
            new CalendarEvent("calendar.family", "evt-1", null, "Dentist", Nine, Ten, null, null));

    private readonly FakeCalendarSource _caldav = new FakeCalendarSource("caldav:")
        .WithCalendar("caldav:nextcloud/personal", "Personal", CalendarCapabilities.Create | CalendarCapabilities.Update | CalendarCapabilities.UpdateOccurrence | CalendarCapabilities.Attendees);

    private readonly ListLogger<CompositeCalendarProvider> _log = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListCalendars_BothProviders()
    {
        IReadOnlyList<CalendarInfo> calendars = (await Composite().ListCalendarsAsync(Ct)).Value;

        Assert.Equal(["calendar.family", "caldav:nextcloud/personal"], calendars.Select(calendar => calendar.Id));
        Assert.True(calendars[1].Capabilities.HasFlag(CalendarCapabilities.UpdateOccurrence));
    }

    [Fact]
    public void Overview_CountsAndCombinesCapabilities()
    {
        CompositeCalendarProvider composite = Composite();

        Assert.True(composite.IsConfigured);
        Assert.Equal(2, composite.Overview.Count);
        Assert.True(composite.Overview.Capabilities.HasFlag(CalendarCapabilities.UpdateOccurrence));
    }

    [Fact]
    public async Task Overview_SkipsUnconfiguredSources()
    {
        _ha.IsConfigured = false;
        CompositeCalendarProvider composite = Composite();

        Assert.Equal(1, composite.Overview.Count);
        Assert.Equal(["caldav:nextcloud/personal"], (await composite.ListCalendarsAsync(Ct)).Value.Select(calendar => calendar.Id));
    }

    [Fact]
    public void NoConfiguredSource_NotConfigured()
    {
        _ha.IsConfigured = false;
        _caldav.IsConfigured = false;

        Assert.False(Composite().IsConfigured);
    }

    [Fact]
    public async Task RoutesByCalendarId()
    {
        CompositeCalendarProvider composite = Composite();
        var zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
        var data = new CalendarEventData("Climbing", Nine, Ten);

        Assert.Single((await composite.ListEventsAsync("calendar.family", Nine.DateTime!.Value.AddHours(-1), Ten.DateTime!.Value, zone, Ct)).Value);
        Assert.True((await composite.CreateEventAsync("caldav:nextcloud/personal", data, Ct)).IsSuccess);
        Assert.True((await composite.UpdateEventAsync("caldav:nextcloud/personal", "uid-1", "20261022T090000", data, CalendarUpdateScope.Occurrence, Ct)).IsSuccess);

        Assert.Equal(["calendar.family"], _ha.EventQueries.Select(query => query.Calendar));
        Assert.Empty(_ha.Created);
        Assert.Equal("caldav:nextcloud/personal", Assert.Single(_caldav.Created).Calendar);
        Assert.Equal(CalendarUpdateScope.Occurrence, Assert.Single(_caldav.Updated).Scope);
    }

    [Fact]
    public async Task UnknownCalendar_NotFoundWithoutCalls()
    {
        CompositeCalendarProvider composite = Composite();

        Result<IReadOnlyList<CalendarEvent>> events = await composite.ListEventsAsync("ical:other", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), TimeZoneInfo.Utc, Ct);
        Result created = await composite.CreateEventAsync("ical:other", new CalendarEventData("x", Nine, Ten), Ct);

        Assert.Equal(("calendar.not_found", ErrorType.NotFound), (events.Error.Code, events.Error.Type));
        Assert.Equal("calendar.not_found", created.Error.Code);
        Assert.Empty(_ha.EventQueries);
        Assert.Empty(_caldav.EventQueries);
    }

    [Fact]
    public async Task OneSourceFails_OthersCalendarsWithAWarning()
    {
        _caldav.Failure = CalendarErrors.Failed("CalDAV server unreachable");

        IReadOnlyList<CalendarInfo> calendars = (await Composite().ListCalendarsAsync(Ct)).Value;

        Assert.Equal(["calendar.family"], calendars.Select(calendar => calendar.Id));
        Assert.Contains(_log.Entries, entry => entry.StartsWith("Warning: ", StringComparison.Ordinal) && entry.Contains("CalDAV server unreachable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task EverySourceFails_Fails()
    {
        _ha.Failure = CalendarErrors.Failed("HA unreachable");
        _caldav.Failure = CalendarErrors.Failed("CalDAV server unreachable");

        Assert.Equal("calendar.failed", (await Composite().ListCalendarsAsync(Ct)).Error.Code);
    }

    private CompositeCalendarProvider Composite() => new([_ha, _caldav], _log);
}
