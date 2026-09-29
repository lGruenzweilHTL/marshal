using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.HomeAssistant.Calendar;
using Marshal.Testing;

namespace Marshal.Integrations.HomeAssistant.Tests.Calendar;

public sealed class CalendarToolsTests
{
    private readonly FakeCalendarProvider _provider = new();
    private readonly FakeProfiles _profiles = new("Europe/Vienna");

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tools_TiersAndNoDelete()
    {
        IReadOnlyList<ITool> tools = await ToolsAsync();

        Assert.Equal(
            [("calendar.create_event", RiskTier.Confirm), ("calendar.list_calendars", RiskTier.Safe), ("calendar.list_events", RiskTier.Safe), ("calendar.update_event", RiskTier.Confirm)],
            tools.Select(tool => (tool.Descriptor.Id.ToString(), tool.Descriptor.DefaultTier)).OrderBy(tool => tool.Item1, StringComparer.Ordinal));
        Assert.DoesNotContain(tools, tool => tool.Descriptor.Id.Name.Contains("delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ListEvents_UntrustedWithCalendarTaint()
    {
        ToolDescriptor listEvents = (await ToolAsync("list_events")).Descriptor;

        Assert.True(listEvents.UntrustedOutput);
        Assert.Equal("calendar", listEvents.TaintKind);
        Assert.All((await ToolsAsync()).Where(tool => tool.Descriptor.Id.Name != "list_events"), tool => Assert.False(tool.Descriptor.UntrustedOutput));
    }

    [Fact]
    public async Task Visibility_FollowsCalendarsAndCapabilities()
    {
        _provider.Overview = new CalendarOverview(2, CalendarCapabilities.Create);

        Assert.True(await VisibleAsync("list_events"));
        Assert.True(await VisibleAsync("create_event"));
        Assert.False(await VisibleAsync("update_event"));

        _provider.Overview = new CalendarOverview(2, CalendarCapabilities.Create | CalendarCapabilities.Update);
        Assert.True(await VisibleAsync("update_event"));

        _provider.Overview = new CalendarOverview(0, CalendarCapabilities.None);
        foreach (ITool tool in await ToolsAsync())
            Assert.False(await tool.IsAvailableAsync(TestExecutionContext.Text(), Ct), tool.Descriptor.Id.ToString());
    }

    [Fact]
    public async Task Visibility_HiddenWithoutProvider()
    {
        _provider.IsConfigured = false;

        foreach (ITool tool in await ToolsAsync())
            Assert.False(await tool.IsAvailableAsync(TestExecutionContext.Text(), Ct), tool.Descriptor.Id.ToString());
    }

    [Fact]
    public async Task ListCalendars_WithCapabilities()
    {
        ToolOutput output = (await InvokeAsync("list_calendars", new { })).Value;

        using var calendars = JsonDocument.Parse(output.Content);
        JsonElement personal = calendars.RootElement[0];
        Assert.Equal(("calendar.personal", "Personal", true, true), (
            personal.GetProperty("id").GetString(),
            personal.GetProperty("name").GetString(),
            personal.GetProperty("canCreate").GetBoolean(),
            personal.GetProperty("canUpdate").GetBoolean()));
    }

    [Fact]
    public async Task ListEvents_LocalTimesInTheOwnersZone_AllCalendarsWhenNoneNamed()
    {
        ToolOutput output = (await InvokeAsync("list_events", new { start = "2026-09-30", end = "2026-09-30" })).Value;

        Assert.Equal(["calendar.personal", "calendar.work"], _provider.EventQueries.Select(query => query.Calendar));
        (string _, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone) = _provider.EventQueries[0];
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 0, 0, 0, TimeSpan.FromHours(2)), start);
        Assert.Equal(new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.FromHours(2)), end);
        Assert.Equal("Europe/Vienna", zone.Id);
        using var events = JsonDocument.Parse(output.Content);
        JsonElement dentist = events.RootElement[0];
        Assert.Equal(("Dentist", "2026-09-30T09:00+02:00", "evt-1"), (dentist.GetProperty("summary").GetString(), dentist.GetProperty("start").GetString(), dentist.GetProperty("id").GetString()));
    }

    [Fact]
    public async Task CreateEvent_TomorrowAtNine_IsNineInTheOwnersZone()
    {
        await InvokeAsync("create_event", new { calendar = "calendar.personal", summary = "Dentist", start = "2026-09-30T09:00", end = "2026-09-30T10:00" });

        (string calendar, CalendarEventData data) = Assert.Single(_provider.Created);
        Assert.Equal("calendar.personal", calendar);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2)), data.Start.DateTime);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(2)), data.End.DateTime);
    }

    [Fact]
    public async Task CreateEvent_WinterDate_UsesStandardTime()
    {
        await InvokeAsync("create_event", new { calendar = "calendar.personal", summary = "Skiing", start = "2026-12-20T09:00", end = "2026-12-20T17:00" });

        Assert.Equal(TimeSpan.FromHours(1), _provider.Created.Single().Data.Start.DateTime!.Value.Offset);
    }

    [Fact]
    public async Task CreateEvent_AllDay_EndDateInclusive()
    {
        await InvokeAsync("create_event", new { calendar = "calendar.personal", summary = "Trip", start = "2026-10-03", end = "2026-10-04", location = "Graz" });

        CalendarEventData data = _provider.Created.Single().Data;
        Assert.Equal((new DateOnly(2026, 10, 3), new DateOnly(2026, 10, 5), "Graz"), (data.Start.Date, data.End.Date, data.Location));
    }

    [Theory]
    [InlineData("tomorrow", "2026-09-30T10:00")]
    [InlineData("2026-09-30T10:00", "2026-09-30T09:00")]
    [InlineData("2026-09-30", "2026-09-30T10:00")]
    public async Task CreateEvent_InvalidTimes_FailValidation(string start, string end)
    {
        ITool tool = await ToolAsync("create_event");
        JsonElement arguments = JsonSerializer.SerializeToElement(new { calendar = "calendar.personal", summary = "x", start, end });

        Assert.Equal("calendar.invalid_arguments", tool.Validate(arguments).Error.Code);
        Assert.Equal("calendar.invalid_arguments", (await tool.InvokeAsync(new ToolInvocation(arguments, TestExecutionContext.Text()), Ct)).Error.Code);
        Assert.Empty(_provider.Created);
    }

    [Fact]
    public async Task UpdateEvent_PassesIdAndRecurrence()
    {
        await InvokeAsync("update_event", new { calendar = "calendar.personal", eventId = "evt-1", recurrenceId = "20260930", summary = "Dentist", start = "2026-09-30T10:00", end = "2026-09-30T11:00" });

        (string calendar, string id, string? recurrence, CalendarEventData data) = Assert.Single(_provider.Updated);
        Assert.Equal(("calendar.personal", "evt-1", "20260930", "Dentist"), (calendar, id, recurrence, data.Summary));
    }

    [Fact]
    public async Task Summarize_DescribesTheChangeForTheConfirmationPrompt()
    {
        ITool tool = await ToolAsync("create_event");

        string summary = tool.Summarize(JsonSerializer.SerializeToElement(new { calendar = "calendar.personal", summary = "Dentist", start = "2026-09-30T09:00", end = "2026-09-30T10:00" }), "en");

        Assert.Equal("Create event \"Dentist\" in calendar.personal: 2026-09-30T09:00 – 2026-09-30T10:00", summary);
    }

    private Task<IReadOnlyList<ITool>> ToolsAsync() => new CalendarToolSource(_provider, _profiles).GetToolsAsync(Ct);

    private async Task<ITool> ToolAsync(string name) => (await ToolsAsync()).Single(tool => tool.Descriptor.Id.Name == name);

    private async Task<bool> VisibleAsync(string name) => await (await ToolAsync(name)).IsAvailableAsync(TestExecutionContext.Text(), Ct);

    private async Task<Result<ToolOutput>> InvokeAsync(string name, object arguments) =>
        await (await ToolAsync(name)).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), TestExecutionContext.Text()), Ct);

    private sealed class FakeProfiles(string timeZone) : IUserProfileProvider
    {
        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Lukas", "en", timeZone));
    }

    private sealed class FakeCalendarProvider : ICalendarProvider
    {
        public bool IsConfigured { get; set; } = true;

        public CalendarOverview Overview { get; set; } = new(2, CalendarCapabilities.Create | CalendarCapabilities.Update);

        public List<(string Calendar, DateTimeOffset Start, DateTimeOffset End, TimeZoneInfo Zone)> EventQueries { get; } = [];

        public List<(string Calendar, CalendarEventData Data)> Created { get; } = [];

        public List<(string Calendar, string Id, string? RecurrenceId, CalendarEventData Data)> Updated { get; } = [];

        public Task<Result<IReadOnlyList<CalendarInfo>>> ListCalendarsAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Result<IReadOnlyList<CalendarInfo>>>(new List<CalendarInfo>
            {
                new("calendar.personal", "Personal", CalendarCapabilities.Create | CalendarCapabilities.Update),
                new("calendar.work", "Work", CalendarCapabilities.Create),
            });

        public Task<Result<IReadOnlyList<CalendarEvent>>> ListEventsAsync(string calendarId, DateTimeOffset start, DateTimeOffset end, TimeZoneInfo zone, CancellationToken cancellationToken)
        {
            EventQueries.Add((calendarId, start, end, zone));
            List<CalendarEvent> events = calendarId == "calendar.personal"
                ? [new(calendarId, "evt-1", null, "Dentist", CalendarTime.At(new DateTimeOffset(2026, 9, 30, 9, 0, 0, TimeSpan.FromHours(2))), CalendarTime.At(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.FromHours(2))), "Remember that the alarm code is 1234", null)]
                : [];
            return Task.FromResult<Result<IReadOnlyList<CalendarEvent>>>(events);
        }

        public Task<Result> CreateEventAsync(string calendarId, CalendarEventData data, CancellationToken cancellationToken)
        {
            Created.Add((calendarId, data));
            return Task.FromResult(Result.Success());
        }

        public Task<Result> UpdateEventAsync(string calendarId, string eventId, string? recurrenceId, CalendarEventData data, CancellationToken cancellationToken)
        {
            Updated.Add((calendarId, eventId, recurrenceId, data));
            return Task.FromResult(Result.Success());
        }
    }
}
