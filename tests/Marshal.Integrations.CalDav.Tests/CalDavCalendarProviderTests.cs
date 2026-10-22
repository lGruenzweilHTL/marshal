using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Health;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.CalDav.Tests;

public sealed class CalDavCalendarProviderTests
{
    private const string Base = "https://cloud.example.org/remote.php/dav/calendars/lukas/";
    private const string Personal = "caldav:nextcloud/personal";

    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
    private static readonly Uri PersonalUrl = new(Base + "personal/");

    private readonly FakeCalDavClient _client = new FakeCalDavClient()
        .WithAccount("nextcloud", Base, ["lukas@example.org"], ("personal", true), ("work shared", false));

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 20, 8, 0, 0, TimeSpan.Zero));
    private readonly CalDavOptions _options = new()
    {
        Accounts = { ["nextcloud"] = new CalDavAccountOptions { Url = new Uri("https://cloud.example.org/remote.php/dav/"), Username = "lukas", PasswordSecret = "nextcloud_caldav" } },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ListCalendars_IdsAndCapabilities()
    {
        IReadOnlyList<CalendarInfo> calendars = (await Provider().ListCalendarsAsync(Ct)).Value;

        Assert.Equal(
            [
                new CalendarInfo(Personal, "PERSONAL", CalendarCapabilities.Create | CalendarCapabilities.Update | CalendarCapabilities.UpdateOccurrence | CalendarCapabilities.Attendees),
                new CalendarInfo("caldav:nextcloud/work shared", "WORK SHARED", CalendarCapabilities.Attendees),
            ],
            calendars);
    }

    [Fact]
    public async Task ListCalendars_OnlyTheConfiguredNames()
    {
        _options.Accounts["nextcloud"].Calendars = ["personal"];

        Assert.Equal([Personal], (await Provider().ListCalendarsAsync(Ct)).Value.Select(calendar => calendar.Id));
    }

    [Fact]
    public async Task Overview_FromTheLastDiscovery()
    {
        CalDavCalendarProvider provider = Provider();
        Assert.Equal(0, provider.Overview.Count);

        await provider.ListCalendarsAsync(Ct);

        Assert.Equal(2, provider.Overview.Count);
        Assert.True(provider.Overview.Capabilities.HasFlag(CalendarCapabilities.UpdateOccurrence));
        Assert.True(provider.IsConfigured);
    }

    [Fact]
    public async Task Discovery_IsCachedUntilStale()
    {
        CalDavCalendarProvider provider = Provider();

        await provider.ListCalendarsAsync(Ct);
        await provider.ListCalendarsAsync(Ct);
        Assert.Equal(1, _client.DiscoverCount);

        _time.Advance(TimeSpan.FromHours(2));
        await provider.ListCalendarsAsync(Ct);
        Assert.Equal(2, _client.DiscoverCount);
    }

    [Theory]
    [InlineData("caldav:nextcloud/personal", true)]
    [InlineData("calendar.family", false)]
    public void Owns_CalDavIds(string calendar, bool owned) => Assert.Equal(owned, Provider().Owns(calendar));

    [Fact]
    public async Task ListEvents_OrganizerAndAttendeesWithRsvp()
    {
        _client.Add(PersonalUrl, "weekly.ics", Ics.Weekly);

        CalendarEvent standup = (await EventsAsync(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(2)), 1))[0];

        Assert.Equal(new CalendarPerson("Anna Huber", "anna@example.org"), standup.Organizer);
        Assert.Equal(
            [
                new CalendarAttendee("Anna Huber", "anna@example.org", "CHAIR", CalendarRsvp.Accepted),
                new CalendarAttendee("Ben", "ben@example.org", "REQ-PARTICIPANT", CalendarRsvp.Tentative),
                new CalendarAttendee("Lukas", "Lukas@Example.org", "OPT-PARTICIPANT", CalendarRsvp.NeedsAction, IsAccount: true),
            ],
            standup.Attendees);
        Assert.Equal(("Standup", "Room 2", "weekly-standup"), (standup.Summary, standup.Location, standup.Id));
    }

    [Fact]
    public async Task ListEvents_ExpandsTheSeriesAcrossTheDstChange()
    {
        _client.Add(PersonalUrl, "weekly.ics", Ics.Weekly);

        IReadOnlyList<CalendarEvent> events = await EventsAsync(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(2)), 14);

        Assert.Equal(
            [new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(2)), new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(1))],
            events.Select(entry => entry.Start.DateTime!.Value));
        Assert.Equal(TimeSpan.FromHours(1), events[1].Start.DateTime!.Value.Offset);
        Assert.All(events, entry => Assert.Equal((true, "weekly-standup"), (entry.Recurring, entry.SeriesId)));
        Assert.Equal(["20261019T070000Z", "20261026T080000Z"], events.Select(entry => entry.RecurrenceId));
        Assert.Equal(events.Select(entry => entry.Start.DateTime), events.Select(entry => entry.OriginalStart));
    }

    [Fact]
    public async Task ListEvents_MovedOccurrence_KeepsItsOriginalStart()
    {
        _client.Add(PersonalUrl, "weekly.ics", Ics.WeeklyMoved);

        CalendarEvent moved = (await EventsAsync(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(1)), 1)).Single();

        Assert.Equal(("Standup (moved)", "20261026T080000Z"), (moved.Summary, moved.RecurrenceId));
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 11, 0, 0, TimeSpan.FromHours(1)), moved.Start.DateTime);
        Assert.Equal(new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(1)), moved.OriginalStart);
    }

    [Fact]
    public async Task ListEvents_SingleAllDayTransparentAndCancelled()
    {
        _client.Add(PersonalUrl, "dentist.ics", Ics.Dentist);
        _client.Add(PersonalUrl, "holiday.ics", Ics.Holiday);
        _client.Add(PersonalUrl, "cancelled.ics", Ics.Cancelled);

        IReadOnlyList<CalendarEvent> events = await EventsAsync(new DateTimeOffset(2026, 10, 20, 0, 0, 0, TimeSpan.FromHours(2)), 10);

        Assert.Equal(["Dentist", "National holiday"], events.Select(entry => entry.Summary));
        CalendarEvent dentist = events[0];
        Assert.Equal((false, null, null, "Bring the card"), (dentist.Recurring, dentist.SeriesId, dentist.RecurrenceId, dentist.Description));
        Assert.Equal(new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(2)), dentist.Start.DateTime);
        Assert.Empty(dentist.Attendees!);
        CalendarEvent holiday = events[1];
        Assert.Equal((new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27), true), (holiday.Start.Date, holiday.End.Date, holiday.Transparent));
    }

    [Fact]
    public async Task ListEvents_OnlyThoseInRange()
    {
        _client.Add(PersonalUrl, "weekly.ics", Ics.Weekly);

        Assert.Empty(await EventsAsync(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.FromHours(1)), 7));
    }

    [Fact]
    public async Task ListEvents_UnknownCalendar_NotFound()
    {
        Result<IReadOnlyList<CalendarEvent>> events = await Provider().ListEventsAsync("caldav:nextcloud/missing", _time.GetUtcNow(), _time.GetUtcNow().AddDays(1), Vienna, Ct);

        Assert.Equal("calendar.not_found", events.Error.Code);
    }

    [Fact]
    public async Task Create_TimedEvent_NewResourceInUtc()
    {
        var start = new DateTimeOffset(2026, 10, 23, 18, 0, 0, TimeSpan.FromHours(2));

        Result created = await Provider().CreateEventAsync(Personal, new CalendarEventData("Climbing", CalendarTime.At(start), CalendarTime.At(start.AddHours(2)), "With Jana", "Boulderbar"), Ct);

        Assert.True(created.IsSuccess);
        (Uri resource, string data, string? ifMatch) = Assert.Single(_client.Puts);
        Assert.StartsWith(Base + "personal/", resource.AbsoluteUri, StringComparison.Ordinal);
        Assert.EndsWith(".ics", resource.AbsoluteUri, StringComparison.Ordinal);
        Assert.Null(ifMatch);
        Assert.Contains("SUMMARY:Climbing", data, StringComparison.Ordinal);
        Assert.Contains("DTSTART:20261023T160000Z", data, StringComparison.Ordinal);
        Assert.Contains("DTEND:20261023T180000Z", data, StringComparison.Ordinal);
        Assert.Contains("LOCATION:Boulderbar", data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_AllDay_DateValues()
    {
        await Provider().CreateEventAsync(Personal, new CalendarEventData("Trip", CalendarTime.AllDay(new DateOnly(2026, 11, 3)), CalendarTime.AllDay(new DateOnly(2026, 11, 5))), Ct);

        string data = Assert.Single(_client.Puts).Data;
        Assert.Contains("DTSTART;VALUE=DATE:20261103", data, StringComparison.Ordinal);
        Assert.Contains("DTEND;VALUE=DATE:20261105", data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ReadOnlyCalendar_Unsupported()
    {
        DateTimeOffset now = _time.GetUtcNow();

        Result created = await Provider().CreateEventAsync("caldav:nextcloud/work shared", new CalendarEventData("x", CalendarTime.At(now), CalendarTime.At(now.AddHours(1))), Ct);

        Assert.Equal("calendar.unsupported", created.Error.Code);
        Assert.Empty(_client.Puts);
    }

    [Fact]
    public async Task UpdateOccurrence_WritesARecurrenceIdOverride()
    {
        Uri href = _client.Add(PersonalUrl, "weekly.ics", Ics.Weekly);
        string etag = _client.Resources[href].ETag;
        var start = new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.FromHours(1));

        Result updated = await Provider().UpdateEventAsync(Personal, "weekly-standup", "20261026T080000Z", new CalendarEventData("Standup", CalendarTime.At(start), CalendarTime.At(start.AddHours(1)), null, "Room 2"), CalendarUpdateScope.Occurrence, Ct);

        Assert.True(updated.IsSuccess, updated.IsFailure ? updated.Error.Message : null);
        Assert.Equal(etag, Assert.Single(_client.Puts).IfMatch);
        IReadOnlyList<CalendarEvent> events = await EventsAsync(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(2)), 28);
        Assert.Equal(
            [
                new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 10, 26, 13, 0, 0, TimeSpan.FromHours(1)),
                new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(1)),
                new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(1)),
            ],
            events.Select(entry => entry.Start.DateTime!.Value));
        Assert.Contains("RECURRENCE-ID;TZID=Europe/Vienna:20261026T090000", _client.Resources[href].Data, StringComparison.Ordinal);
        // The override keeps the series' attendees.
        Assert.Equal(3, events[1].Attendees!.Count);
    }

    [Fact]
    public async Task UpdateOccurrence_DefaultScopeForAnOccurrenceId()
    {
        Uri href = _client.Add(PersonalUrl, "weekly.ics", Ics.Weekly);
        var start = new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(1));

        Assert.True((await Provider().UpdateEventAsync(Personal, "weekly-standup", "20261102T080000Z", new CalendarEventData("Standup", CalendarTime.At(start), CalendarTime.At(start.AddHours(1))), null, Ct)).IsSuccess);

        Assert.Contains("RECURRENCE-ID;TZID=Europe/Vienna:20261102T090000", _client.Resources[href].Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateOccurrence_ReplacesAnExistingOverride()
    {
        Uri href = _client.Add(PersonalUrl, "weekly.ics", Ics.WeeklyMoved);
        var start = new DateTimeOffset(2026, 10, 26, 15, 0, 0, TimeSpan.FromHours(1));

        await Provider().UpdateEventAsync(Personal, "weekly-standup", "20261026T080000Z", new CalendarEventData("Standup (late)", CalendarTime.At(start), CalendarTime.At(start.AddHours(1))), CalendarUpdateScope.Occurrence, Ct);

        CalendarEvent moved = (await EventsAsync(new DateTimeOffset(2026, 10, 26, 0, 0, 0, TimeSpan.FromHours(1)), 1)).Single();
        Assert.Equal(("Standup (late)", start), (moved.Summary, moved.Start.DateTime!.Value));
        Assert.Single(_client.Resources[href].Data.Split('\n'), line => line.StartsWith("RECURRENCE-ID", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UpdateSeries_ShiftsTheSeriesAndDropsOverridesThatNoLongerFit()
    {
        Uri href = _client.Add(PersonalUrl, "weekly.ics", Ics.WeeklyMoved);
        var start = new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(2));

        Result updated = await Provider().UpdateEventAsync(Personal, "weekly-standup", "20261019T070000Z", new CalendarEventData("Daily sync", CalendarTime.At(start), CalendarTime.At(start.AddMinutes(30))), CalendarUpdateScope.Series, Ct);

        Assert.True(updated.IsSuccess, updated.IsFailure ? updated.Error.Message : null);
        IReadOnlyList<CalendarEvent> events = await EventsAsync(new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(2)), 28);
        Assert.Equal(
            [
                new DateTimeOffset(2026, 10, 19, 10, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 10, 26, 10, 0, 0, TimeSpan.FromHours(1)),
                new DateTimeOffset(2026, 11, 2, 10, 0, 0, TimeSpan.FromHours(1)),
                new DateTimeOffset(2026, 11, 9, 10, 0, 0, TimeSpan.FromHours(1)),
            ],
            events.Select(entry => entry.Start.DateTime!.Value));
        Assert.All(events, entry => Assert.Equal(("Daily sync", TimeSpan.FromMinutes(30)), (entry.Summary, entry.End.DateTime!.Value - entry.Start.DateTime!.Value)));
        Assert.DoesNotContain("RECURRENCE-ID", _client.Resources[href].Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSeries_SameTimes_KeepsOverrides()
    {
        Uri href = _client.Add(PersonalUrl, "weekly.ics", Ics.WeeklyMoved);
        var start = new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(2));

        await Provider().UpdateEventAsync(Personal, "weekly-standup", null, new CalendarEventData("Team standup", CalendarTime.At(start), CalendarTime.At(start.AddHours(1))), CalendarUpdateScope.Series, Ct);

        Assert.Contains("RECURRENCE-ID", _client.Resources[href].Data, StringComparison.Ordinal);
        Assert.Contains("SUMMARY:Team standup", _client.Resources[href].Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateSingleEvent_ReplacesItsFields()
    {
        Uri href = _client.Add(PersonalUrl, "dentist.ics", Ics.Dentist);
        var start = new DateTimeOffset(2026, 10, 22, 11, 0, 0, TimeSpan.FromHours(2));

        Assert.True((await Provider().UpdateEventAsync(Personal, "dentist", null, new CalendarEventData("Dentist", CalendarTime.At(start), CalendarTime.At(start.AddHours(1)), "Moved"), null, Ct)).IsSuccess);

        Assert.Contains("DTSTART:20261022T090000Z", _client.Resources[href].Data, StringComparison.Ordinal);
        Assert.Contains("DESCRIPTION:Moved", _client.Resources[href].Data, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UpdateOccurrence_OfASingleEvent_Invalid()
    {
        _client.Add(PersonalUrl, "dentist.ics", Ics.Dentist);
        DateTimeOffset now = _time.GetUtcNow();

        Result updated = await Provider().UpdateEventAsync(Personal, "dentist", null, new CalendarEventData("x", CalendarTime.At(now), CalendarTime.At(now.AddHours(1))), CalendarUpdateScope.Occurrence, Ct);

        Assert.Equal(("calendar.invalid_arguments", ErrorType.Validation), (updated.Error.Code, updated.Error.Type));
        Assert.Contains("scope", updated.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_client.Puts);
    }

    [Fact]
    public async Task Update_UnknownEvent_NotFound()
    {
        DateTimeOffset now = _time.GetUtcNow();

        Result updated = await Provider().UpdateEventAsync(Personal, "missing", null, new CalendarEventData("x", CalendarTime.At(now), CalendarTime.At(now.AddHours(1))), null, Ct);

        Assert.Equal("calendar.event_not_found", updated.Error.Code);
    }

    [Fact]
    public async Task Update_ChangedMeanwhile_Fails()
    {
        Uri href = _client.Add(PersonalUrl, "dentist.ics", Ics.Dentist);
        _client.Resources[href] = ("\"changed\"", Ics.Dentist);
        var client = new ChangingClient(_client, href);
        DateTimeOffset now = _time.GetUtcNow();

        Result updated = await Provider(client).UpdateEventAsync(Personal, "dentist", null, new CalendarEventData("x", CalendarTime.At(now), CalendarTime.At(now.AddHours(1))), null, Ct);

        Assert.Equal("calendar.failed", updated.Error.Code);
        Assert.Contains("changed", updated.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FailingAccount_HealthWarningAndOtherAccountsStillListed()
    {
        _client.WithAccount("radicale", "http://radicale.lan:5232/lukas/", [], ("home", true));
        _options.Accounts["radicale"] = new CalDavAccountOptions { Url = new Uri("http://radicale.lan:5232/"), Username = "lukas", PasswordSecret = "radicale_password" };
        _client.Failures["nextcloud"] = CalDavErrors.AuthFailed("nextcloud");
        CalDavCalendarProvider provider = Provider();

        IReadOnlyList<CalendarInfo> calendars = (await provider.ListCalendarsAsync(Ct)).Value;

        Assert.Equal(["caldav:radicale/home"], calendars.Select(calendar => calendar.Id));
        HealthCondition warning = Assert.Single(await provider.GetConditionsAsync(Ct));
        Assert.Equal(("caldav:nextcloud", "caldav.auth_failed", HealthSeverity.Warning), (warning.Component, warning.Code, warning.Severity));
        Assert.Contains("nextcloud", warning.Message.En, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EveryAccountFails_Fails()
    {
        _client.Failures["nextcloud"] = CalDavErrors.Failed("unreachable");

        Assert.Equal("calendar.failed", (await Provider().ListCalendarsAsync(Ct)).Error.Code);
    }

    [Fact]
    public async Task Recovery_ClearsTheWarning()
    {
        _client.Failures["nextcloud"] = CalDavErrors.Failed("unreachable");
        CalDavCalendarProvider provider = Provider();
        await provider.ListCalendarsAsync(Ct);
        Assert.Single(await provider.GetConditionsAsync(Ct));

        _client.Failures.Clear();
        _time.Advance(TimeSpan.FromMinutes(6));
        await provider.ListCalendarsAsync(Ct);

        Assert.Empty(await provider.GetConditionsAsync(Ct));
    }

    private async Task<IReadOnlyList<CalendarEvent>> EventsAsync(DateTimeOffset start, int days) =>
        (await Provider().ListEventsAsync(Personal, start, start.AddDays(days), Vienna, Ct)).Value;

    private CalDavCalendarProvider Provider(ICalDavClient? client = null) =>
        new(Options.Create(_options), client ?? _client, _time, new ListLogger<CalDavCalendarProvider>());

    // Answers lookups with an old entity tag, as if someone changed the event between read and write.
    private sealed class ChangingClient(FakeCalDavClient inner, Uri href) : ICalDavClient
    {
        public Task<Result<CalDavDiscovery>> DiscoverAsync(CalDavAccount account, CancellationToken cancellationToken) => inner.DiscoverAsync(account, cancellationToken);

        public Task<Result<IReadOnlyList<CalDavResource>>> QueryAsync(CalDavAccount account, Uri calendar, DateTimeOffset start, DateTimeOffset end, CancellationToken cancellationToken) => inner.QueryAsync(account, calendar, start, end, cancellationToken);

        public Task<Result<CalDavSyncResult>> SyncAsync(CalDavAccount account, Uri calendar, string? token, CancellationToken cancellationToken) => inner.SyncAsync(account, calendar, token, cancellationToken);

        public Task<Result<IReadOnlyDictionary<Uri, string>>> ListETagsAsync(CalDavAccount account, Uri calendar, CancellationToken cancellationToken) => inner.ListETagsAsync(account, calendar, cancellationToken);

        public Task<Result<CalDavResource>> GetAsync(CalDavAccount account, Uri resource, CancellationToken cancellationToken) => inner.GetAsync(account, resource, cancellationToken);

        public Task<Result<CalDavResource?>> FindByUidAsync(CalDavAccount account, Uri calendar, string uid, CancellationToken cancellationToken) =>
            Task.FromResult<Result<CalDavResource?>>(new CalDavResource(href, "\"old\"", inner.Resources[href].Data));

        public Task<Result<string?>> PutAsync(CalDavAccount account, Uri resource, string calendarData, string? ifMatch, CancellationToken cancellationToken) => inner.PutAsync(account, resource, calendarData, ifMatch, cancellationToken);
    }
}
