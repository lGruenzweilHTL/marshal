using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Tools;
using Marshal.Demo.Calendar;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marshal.Demo.Tests;

public sealed class CalDavDemoTests
{
    private const string Work = "caldav:demo/work";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Unconfigured_SimulatedNextToTheHaCalendars()
    {
        await using var demo = DemoServices.Create();

        Assert.Contains(demo.Provider.GetServices<ICalendarSource>(), source => source is DemoCalDavCalendars);
        Assert.Contains(DemoIntegration.Caldav, demo.Get<DemoIntegrations>().SimulatedAtStartup);
        Assert.Contains(demo.Provider.GetServices<IEventSource>(), source => source is DemoCalDavEventSource { Source: "calendar", IsConnected: true });
        IReadOnlyList<CalendarInfo> calendars = (await demo.Get<ICalendarProvider>().ListCalendarsAsync(Ct)).Value;
        Assert.Contains(calendars, calendar => calendar.Id == "calendar.family");
        CalendarInfo work = Assert.Single(calendars, calendar => calendar.Id == Work);
        Assert.True(work.Capabilities.HasFlag(CalendarCapabilities.UpdateOccurrence | CalendarCapabilities.Attendees));
    }

    [Fact]
    public async Task Configured_NotSimulated()
    {
        await using var demo = DemoServices.Create(new() { ["calendar:caldav:accounts:nextcloud:url"] = "https://cloud.example.org/remote.php/dav/" });

        Assert.DoesNotContain(demo.Provider.GetServices<ICalendarSource>(), source => source is DemoCalDavCalendars);
        Assert.DoesNotContain(DemoIntegration.Caldav, demo.Get<DemoIntegrations>().SimulatedAtStartup);
    }

    [Fact]
    public async Task ListEvents_WeeklyStandupWithAttendees()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        string listed = await DemoServices.InvokeAsync(tools["calendar.list_events"], $$"""{"calendar":"{{Work}}","start":"2026-10-12","end":"2026-10-25"}""");

        using var events = JsonDocument.Parse(listed);
        List<JsonElement> standups = [.. events.RootElement.EnumerateArray().Where(item => item.GetProperty("summary").GetString() == "Team standup")];
        Assert.Equal(["2026-10-12T09:00+02:00", "2026-10-19T09:00+02:00"], standups.Select(item => item.GetProperty("start").GetString()));
        Assert.All(standups, item => Assert.True(item.GetProperty("recurring").GetBoolean()));
        Assert.Contains(standups[0].GetProperty("attendees").EnumerateArray(), attendee => attendee.GetProperty("status").GetString() == "tentative");
        Assert.Contains(events.RootElement.EnumerateArray(), item => item.GetProperty("attendees").EnumerateArray().Any(attendee => attendee.GetProperty("status").GetString() == "declined"));
    }

    [Fact]
    public async Task UpdateOccurrence_MovesOnlyThatStandup()
    {
        await using var demo = DemoServices.Create();
        ICalendarProvider calendars = demo.Get<ICalendarProvider>();
        var vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
        var from = new DateTimeOffset(2026, 10, 12, 0, 0, 0, TimeSpan.FromHours(2));
        CalendarEvent second = (await calendars.ListEventsAsync(Work, from, from.AddDays(14), vienna, Ct)).Value.Where(item => item.Summary == "Team standup").ElementAt(1);
        var later = new DateTimeOffset(2026, 10, 19, 11, 0, 0, TimeSpan.FromHours(2));

        Result updated = await calendars.UpdateEventAsync(Work, second.Id!, second.RecurrenceId, new CalendarEventData("Team standup", CalendarTime.At(later), CalendarTime.At(later.AddMinutes(15))), CalendarUpdateScope.Occurrence, Ct);

        Assert.True(updated.IsSuccess);
        IReadOnlyList<CalendarEvent> after = (await calendars.ListEventsAsync(Work, from, from.AddDays(21), vienna, Ct)).Value;
        Assert.Equal(
            [new DateTimeOffset(2026, 10, 12, 9, 0, 0, TimeSpan.FromHours(2)), later, new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(1))],
            after.Where(item => item.Summary == "Team standup").Select(item => item.Start.DateTime!.Value));
    }

    [Fact]
    public async Task Invitation_ArrivesAfterStart_WithIdOnlyPayload()
    {
        await using var demo = DemoServices.Create();
        DemoCalDavEventSource source = demo.Provider.GetServices<IHostedService>().OfType<DemoCalDavEventSource>().Single();
        await source.StartAsync(Ct);
        Assert.DoesNotContain(demo.Events.Events, item => item.Source == "calendar");

        demo.Time.Advance(TimeSpan.FromMinutes(3));

        MarshalEvent invitation = Assert.Single(demo.Events.Events, item => item.Type == "invitation");
        Assert.Equal(("calendar", Work, true), (invitation.Source, invitation.Entity, invitation.Trusted));
        Assert.Equal(["calendar", "end", "eventId", "start"], invitation.Payload.EnumerateObject().Select(property => property.Name).Order(StringComparer.Ordinal));
        Assert.Single(demo.Events.Events, item => item.Type == "event_created");
        var vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");
        IReadOnlyList<CalendarEvent> events = (await demo.Get<ICalendarProvider>().ListEventsAsync(Work, demo.Time.GetUtcNow(), demo.Time.GetUtcNow().AddDays(7), vienna, Ct)).Value;
        CalendarEvent invited = Assert.Single(events, item => item.Id == invitation.Payload.GetProperty("eventId").GetString());
        Assert.Contains(invited.Attendees!, attendee => attendee.IsAccount && attendee.Status == CalendarRsvp.NeedsAction);
        await source.StopAsync(Ct);
    }
}
