using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Integrations.HomeAssistant.Calendar;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Tests.Calendar;

public sealed class HomeAssistantCalendarProviderTests : IAsyncLifetime
{
    // Readable JSON for comparisons ("+" unescaped).
    private static readonly JsonSerializerOptions _plain = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static readonly TimeZoneInfo _vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    private FakeHomeAssistant _ha = null!;
    private HomeAssistantCache _cache = null!;
    private HomeAssistantCalendarProvider _provider = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _ha = await FakeHomeAssistant.StartAsync();
        // supported_features: CREATE_EVENT = 1, DELETE_EVENT = 2, UPDATE_EVENT = 4.
        _ha.AddState("calendar.personal", "off", new JsonObject { ["friendly_name"] = "Personal", ["supported_features"] = 7 });
        _ha.AddState("calendar.work", "off", new JsonObject { ["friendly_name"] = "Work", ["supported_features"] = 1 });
        _ha.AddState("calendar.holidays", "off", new JsonObject { ["friendly_name"] = "Holidays" });

        IOptions<HomeAssistantOptions> options = Options.Create(new HomeAssistantOptions { Url = new Uri("http://homeassistant.local:8123/"), Token = FakeHomeAssistant.Token });
        _cache = new HomeAssistantCache(options, new TestServerSockets(_ha.Server), new HomeAssistantCacheTimings { CommandTimeout = TimeSpan.FromSeconds(2) }, TimeProvider.System, NullLogger<HomeAssistantCache>.Instance);
        await _cache.StartAsync(Ct);
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (_cache.Current is null && DateTime.UtcNow < deadline)
            await Task.Delay(10, Ct);

        _provider = new HomeAssistantCalendarProvider(new HomeAssistantClient(new TestServerHttp(_ha.Server), options), _cache, _cache, options);
    }

    public async ValueTask DisposeAsync()
    {
        await _cache.StopAsync(CancellationToken.None);
        _cache.Dispose();
        await _ha.DisposeAsync();
    }

    [Fact]
    public async Task ListCalendars_FromHaWithCapabilities()
    {
        _ha.Calendars.Add(new JsonObject { ["entity_id"] = "calendar.holidays", ["name"] = "Holidays" });

        IReadOnlyList<CalendarInfo> calendars = (await _provider.ListCalendarsAsync(Ct)).Value;

        Assert.Equal(
            [
                new CalendarInfo("calendar.personal", "Personal", CalendarCapabilities.Create | CalendarCapabilities.Update),
                new CalendarInfo("calendar.work", "Work", CalendarCapabilities.Create),
                new CalendarInfo("calendar.holidays", "Holidays", CalendarCapabilities.None),
            ],
            calendars);
    }

    [Fact]
    public async Task ListEvents_QueriesTheRangeAndReturnsTimesInTheOwnersZone()
    {
        _ha.CalendarEvents["calendar.personal"] = JsonNode.Parse("""
            [
              {"start":{"dateTime":"2026-09-30T07:00:00+00:00"},"end":{"dateTime":"2026-09-30T08:00:00+00:00"},"summary":"Dentist",
               "description":"Bring the card","location":"Main St 1","uid":"evt-1","recurrence_id":null,"rrule":null},
              {"start":{"date":"2026-09-30"},"end":{"date":"2026-10-01"},"summary":"Holiday","description":null,"location":null,
               "uid":"evt-2","recurrence_id":"20260930","rrule":"FREQ=YEARLY"}
            ]
            """)!.AsArray();
        var start = new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.FromHours(2));

        IReadOnlyList<CalendarEvent> events = (await _provider.ListEventsAsync("calendar.personal", start, start.AddDays(1), _vienna, Ct)).Value;

        Assert.Equal("?start=2026-09-30T00%3A00%3A00%2B02%3A00&end=2026-10-01T00%3A00%3A00%2B02%3A00", Assert.Single(_ha.CalendarQueries));
        CalendarEvent dentist = events[0];
        Assert.Equal(("calendar.personal", "evt-1", null, "Dentist", "Bring the card", "Main St 1"), (dentist.CalendarId, dentist.Id, dentist.RecurrenceId, dentist.Summary, dentist.Description, dentist.Location));
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2)), dentist.Start.DateTime);
        Assert.Equal(TimeSpan.FromHours(2), dentist.Start.DateTime!.Value.Offset);
        CalendarEvent holiday = events[1];
        Assert.Equal((new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1), "20260930"), (holiday.Start.Date, holiday.End.Date, holiday.RecurrenceId));
        Assert.Null(holiday.Start.DateTime);
    }

    [Theory]
    [InlineData("light.kitchen")]
    [InlineData("calendar.personal/../../api/states")]
    public async Task ListEvents_NotACalendar_NotFoundWithoutRequest(string id)
    {
        Result<IReadOnlyList<CalendarEvent>> result = await _provider.ListEventsAsync(id, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), _vienna, Ct);

        Assert.Equal("calendar.not_found", result.Error.Code);
        Assert.Empty(_ha.CalendarQueries);
    }

    [Fact]
    public async Task Create_TimedEvent_CallsCreateEventWithOffsets()
    {
        var start = new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2));

        Result created = await _provider.CreateEventAsync("calendar.work", new CalendarEventData("Standup", CalendarTime.At(start), CalendarTime.At(start.AddMinutes(15)), Location: "Room 2"), Ct);

        Assert.True(created.IsSuccess);
        (string path, JsonNode? body) = _ha.RestRequests.Last();
        Assert.Equal("/api/services/calendar/create_event", path);
        Assert.Equal(
            """{"entity_id":"calendar.work","summary":"Standup","start_date_time":"2026-09-30T09:00:00+02:00","end_date_time":"2026-09-30T09:15:00+02:00","location":"Room 2"}""",
            body!.ToJsonString(_plain));
    }

    [Fact]
    public async Task Create_AllDayEvent_UsesDates()
    {
        await _provider.CreateEventAsync("calendar.personal", new CalendarEventData("Trip", CalendarTime.AllDay(new DateOnly(2026, 10, 3)), CalendarTime.AllDay(new DateOnly(2026, 10, 5))), Ct);

        JsonNode body = _ha.RestRequests.Last().Body!;
        Assert.Equal(("2026-10-03", "2026-10-05"), ((string?)body["start_date"], (string?)body["end_date"]));
        Assert.Null(body["start_date_time"]);
    }

    [Fact]
    public async Task Create_CalendarWithoutCreate_UnsupportedWithoutCall()
    {
        _ha.Calendars.Add(new JsonObject { ["entity_id"] = "calendar.holidays", ["name"] = "Holidays" });
        int before = _ha.RestRequests.Count;
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Result created = await _provider.CreateEventAsync("calendar.holidays", new CalendarEventData("x", CalendarTime.At(now), CalendarTime.At(now.AddHours(1))), Ct);

        Assert.Equal("calendar.unsupported", created.Error.Code);
        Assert.DoesNotContain(_ha.RestRequests.Skip(before), request => request.Path.StartsWith("/api/services", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Update_SendsTheWebsocketCommandWithTheWholeEvent()
    {
        var start = new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(2));

        Result updated = await _provider.UpdateEventAsync("calendar.personal", "evt-1", "20260930", new CalendarEventData("Dentist", CalendarTime.At(start), CalendarTime.At(start.AddHours(1)), "Moved"), Ct);

        Assert.True(updated.IsSuccess);
        JsonObject command = _ha.Commands.Last(candidate => (string?)candidate["type"] == "calendar/event/update");
        Assert.Equal(("calendar.personal", "evt-1", "20260930"), ((string?)command["entity_id"], (string?)command["uid"], (string?)command["recurrence_id"]));
        Assert.Equal(
            """{"summary":"Dentist","dtstart":"2026-09-30T10:00:00+02:00","dtend":"2026-09-30T11:00:00+02:00","description":"Moved"}""",
            command["event"]!.ToJsonString(_plain));
    }

    [Fact]
    public async Task Update_CalendarWithoutUpdate_Unsupported()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Result updated = await _provider.UpdateEventAsync("calendar.work", "evt-1", null, new CalendarEventData("x", CalendarTime.At(now), CalendarTime.At(now.AddHours(1))), Ct);

        Assert.Equal("calendar.unsupported", updated.Error.Code);
        Assert.Equal(0, _ha.CountCommands("calendar/event/update"));
    }

    [Fact]
    public async Task Update_HaRejects_Failed()
    {
        _ha.FailingCommands["calendar/event/update"] = "Event not found";
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Result updated = await _provider.UpdateEventAsync("calendar.personal", "missing", null, new CalendarEventData("x", CalendarTime.At(now), CalendarTime.At(now.AddHours(1))), Ct);

        Assert.Equal("calendar.failed", updated.Error.Code);
        Assert.Contains("Event not found", updated.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NotConfigured_WithoutHaUrl()
    {
        IOptions<HomeAssistantOptions> options = Options.Create(new HomeAssistantOptions());
        var provider = new HomeAssistantCalendarProvider(new HomeAssistantClient(new TestServerHttp(_ha.Server), options), _cache, _cache, options);

        Assert.False(provider.IsConfigured);
        Assert.True(_provider.IsConfigured);
    }
}
