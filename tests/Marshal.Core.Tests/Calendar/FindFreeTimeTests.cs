using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;
using Marshal.Core.Calendar;
using Marshal.Testing;

namespace Marshal.Core.Tests.Calendar;

public sealed class FindFreeTimeTests
{
    private static readonly TimeSpan Summer = TimeSpan.FromHours(2);

    private readonly FakeCalendarSource _ha = new FakeCalendarSource()
        .WithCalendar("calendar.family", "Family", CalendarCapabilities.Create);
    private readonly FakeCalendarSource _caldav = new FakeCalendarSource("caldav:")
        .WithCalendar("caldav:nextcloud/personal", "Personal", CalendarCapabilities.Create | CalendarCapabilities.Update | CalendarCapabilities.UpdateOccurrence | CalendarCapabilities.Attendees);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Descriptor_SafeAndTrusted()
    {
        ToolDescriptor descriptor = (await ToolAsync()).Descriptor;

        Assert.Equal((RiskTier.Safe, false, null), (descriptor.DefaultTier, descriptor.UntrustedOutput, descriptor.TaintKind));
    }

    [Fact]
    public async Task Visibility_HiddenWithoutCalendars()
    {
        _ha.IsConfigured = false;
        _caldav.IsConfigured = false;

        Assert.False(await (await ToolAsync()).IsAvailableAsync(TestExecutionContext.Text(), Ct));
    }

    [Fact]
    public async Task TwoFreeHours_BusyMergedAcrossBothProviders()
    {
        Busy(_ha, "calendar.family", 9, 0, 11, 0);
        Busy(_caldav, "caldav:nextcloud/personal", 10, 30, 12, 0);
        Busy(_caldav, "caldav:nextcloud/personal", 14, 0, 15, 0);

        Assert.Equal(["12:00-14:00", "15:00-18:00"], await FreeAsync(new { start = "2026-10-22T08:00", end = "2026-10-22T18:00", duration = 120 }));
    }

    [Fact]
    public async Task ShorterGaps_LeftOut()
    {
        Busy(_ha, "calendar.family", 9, 0, 10, 0);
        Busy(_ha, "calendar.family", 10, 30, 17, 0);

        Assert.Equal(["08:00-09:00", "17:00-18:00"], await FreeAsync(new { start = "2026-10-22T08:00", end = "2026-10-22T18:00", duration = 60 }));
    }

    [Fact]
    public async Task TransparentAndDeclinedEvents_AreFree()
    {
        Busy(_caldav, "caldav:nextcloud/personal", 9, 0, 12, 0, transparent: true);
        Busy(_caldav, "caldav:nextcloud/personal", 16, 0, 17, 0, accountStatus: CalendarRsvp.Declined);
        Busy(_caldav, "caldav:nextcloud/personal", 13, 0, 14, 0, accountStatus: CalendarRsvp.Tentative);

        Assert.Equal(["08:00-13:00", "14:00-18:00"], await FreeAsync(new { start = "2026-10-22T08:00", end = "2026-10-22T18:00", duration = 30 }));
    }

    [Fact]
    public async Task Within_ClipsEachDay()
    {
        Busy(_ha, "calendar.family", 8, 0, 9, 0);

        Assert.Equal(
            ["2026-10-22 09:00-18:00", "2026-10-23 08:00-18:00"],
            await FreeAsync(new { start = "2026-10-22", end = "2026-10-23", duration = 60, within = "08:00-18:00" }, withDate: true));
    }

    [Fact]
    public async Task AllDayEvent_BlocksTheDay()
    {
        _ha.Events["calendar.family"].Add(new CalendarEvent("calendar.family", "trip", null, "Trip", CalendarTime.AllDay(new DateOnly(2026, 10, 22)), CalendarTime.AllDay(new DateOnly(2026, 10, 23)), null, null));

        Assert.Equal(
            ["2026-10-23 08:00-18:00"],
            await FreeAsync(new { start = "2026-10-22", end = "2026-10-23", duration = 60, within = "08:00-18:00" }, withDate: true));
    }

    [Fact]
    public async Task Calendars_LimitsWhichAreRead()
    {
        Busy(_ha, "calendar.family", 9, 0, 17, 0);

        Assert.Equal(["08:00-18:00"], await FreeAsync(new { start = "2026-10-22T08:00", end = "2026-10-22T18:00", duration = 60, calendars = new[] { "caldav:nextcloud/personal" } }));
        Assert.Equal(["caldav:nextcloud/personal"], _caldav.EventQueries.Select(query => query.Calendar));
        Assert.Empty(_ha.EventQueries);
    }

    [Fact]
    public async Task AtMostTwentyIntervals_EarliestFirst()
    {
        for (int hour = 0; hour < 24; hour++)
            Busy(_ha, "calendar.family", hour, 30, hour + 1, 0);

        List<string> free = await FreeAsync(new { start = "2026-10-22T00:00", end = "2026-10-23T00:00", duration = 15 });

        Assert.Equal(20, free.Count);
        Assert.Equal("00:00-00:30", free[0]);
        Assert.Equal("19:00-19:30", free[^1]);
    }

    [Fact]
    public async Task OwnerTimeZone_LocalTimesWithOffset()
    {
        ToolOutput output = (await InvokeAsync(new { start = "2026-10-22T08:00", end = "2026-10-22T10:00", duration = 60 })).Value;

        using var document = JsonDocument.Parse(output.Content);
        JsonElement slot = Assert.Single(document.RootElement.GetProperty("free").EnumerateArray());
        Assert.Equal(("2026-10-22T08:00+02:00", "2026-10-22T10:00+02:00"), (slot.GetProperty("start").GetString(), slot.GetProperty("end").GetString()));
        Assert.Equal(["calendar.family", "caldav:nextcloud/personal"], document.RootElement.GetProperty("calendars").EnumerateArray().Select(item => item.GetString()));
    }

    [Theory]
    [InlineData("""{"start":"2026-10-22T08:00","end":"2026-10-22T18:00"}""")]
    [InlineData("""{"start":"2026-10-22T08:00","end":"2026-10-22T18:00","duration":0}""")]
    [InlineData("""{"start":"2026-10-22T18:00","end":"2026-10-22T08:00","duration":30}""")]
    [InlineData("""{"start":"2026-10-22T08:00","end":"2026-10-22T18:00","duration":30,"within":"18:00-08:00"}""")]
    [InlineData("""{"start":"2026-10-22T08:00","end":"2026-10-22T18:00","duration":30,"within":"morning"}""")]
    [InlineData("""{"start":"2026-10-22T08:00","end":"2026-10-22T18:00","duration":30,"calendars":"calendar.family"}""")]
    public async Task InvalidArguments_FailValidation(string arguments)
    {
        Assert.Equal("calendar.invalid_arguments", (await ToolAsync()).Validate(JsonDocument.Parse(arguments).RootElement).Error.Code);
    }

    private static void Busy(FakeCalendarSource source, string calendar, int fromHour, int fromMinute, int toHour, int toMinute, bool transparent = false, CalendarRsvp? accountStatus = null)
    {
        DateTimeOffset day = new(2026, 10, 22, 0, 0, 0, Summer);
        source.Events[calendar].Add(new CalendarEvent(calendar, Guid.NewGuid().ToString("N"), null, "Busy", CalendarTime.At(day.AddHours(fromHour).AddMinutes(fromMinute)), CalendarTime.At(day.AddHours(toHour).AddMinutes(toMinute)), null, null)
        {
            Transparent = transparent,
            Attendees = accountStatus is { } status ? [new CalendarAttendee("Lukas", "lukas@example.org", "REQ-PARTICIPANT", status, IsAccount: true)] : [],
        });
    }

    private async Task<List<string>> FreeAsync(object arguments, bool withDate = false)
    {
        Result<ToolOutput> output = await InvokeAsync(arguments);
        Assert.True(output.IsSuccess, output.IsFailure ? output.Error.Message : null);
        using var document = JsonDocument.Parse(output.Value.Content);
        return
        [
            .. document.RootElement.GetProperty("free").EnumerateArray().Select(slot =>
            {
                string start = slot.GetProperty("start").GetString()!;
                string end = slot.GetProperty("end").GetString()!;
                string span = $"{start[11..16]}-{end[11..16]}";
                return withDate ? $"{start[..10]} {span}" : span;
            }),
        ];
    }

    private async Task<ITool> ToolAsync()
    {
        var provider = new CompositeCalendarProvider([_ha, _caldav], new ListLogger<CompositeCalendarProvider>());
        IReadOnlyList<ITool> tools = await new CalendarToolSource(provider, new FakeProfiles()).GetToolsAsync(Ct);
        return tools.Single(tool => tool.Descriptor.Id.Name == "find_free_time");
    }

    private async Task<Result<ToolOutput>> InvokeAsync(object arguments) =>
        await (await ToolAsync()).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), TestExecutionContext.Text()), Ct);

    private sealed class FakeProfiles : IUserProfileProvider
    {
        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Lukas", "en", "Europe/Vienna"));
    }
}
