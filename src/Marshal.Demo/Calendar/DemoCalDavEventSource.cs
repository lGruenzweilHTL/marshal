using System.Text.Json;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Microsoft.Extensions.Hosting;

namespace Marshal.Demo.Calendar;

/// <summary>
/// The simulated CalDAV change events: two minutes after start an invitation to a project kickoff arrives in
/// <c>caldav:demo/work</c> and is published as <c>calendar.event_created</c> and <c>calendar.invitation</c>, with the
/// same id-only trusted payload as the real poller, so watches on invitations can be tried without a server.
/// </summary>
public sealed class DemoCalDavEventSource(DemoCalDavCalendars calendars, DemoWorld world, IInstanceOwner owner, IEventPublisher publisher) : IEventSource, IHostedService, IDisposable
{
    public const string Name = "calendar";

    private static readonly TimeSpan Delay = TimeSpan.FromMinutes(2);

    private ITimer? _timer;

    public string Source => Name;

    public bool IsConnected => true;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _timer = world.Time.CreateTimer(_ => _ = InviteAsync(), null, Delay, Timeout.InfiniteTimeSpan);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _timer?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose() => _timer?.Dispose();

    private async Task InviteAsync()
    {
        OwnerId ownerId = await owner.GetAsync(CancellationToken.None);
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(world.Time.GetUtcNow(), TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna")).DateTime);
        DateTimeOffset start = calendars.At(today.AddDays(2), new TimeOnly(10, 0));
        const string Id = "project-kickoff";
        calendars.Add(new CalendarEvent(DemoCalDavCalendars.Work, Id, null, "Project kickoff", CalendarTime.At(start), CalendarTime.At(start.AddHours(1)), "Agenda: goals, roles, first milestone.", "Room 4")
        {
            Organizer = new CalendarPerson("Anna Huber", "anna@demo.invalid"),
            Attendees =
            [
                new CalendarAttendee("Anna Huber", "anna@demo.invalid", "CHAIR", CalendarRsvp.Accepted),
                new CalendarAttendee("You", DemoCalDavCalendars.Account, "REQ-PARTICIPANT", CalendarRsvp.NeedsAction, IsAccount: true),
            ],
        });

        JsonElement payload = JsonSerializer.SerializeToElement(new { calendar = DemoCalDavCalendars.Work, eventId = Id, start = start.ToUniversalTime(), end = start.AddHours(1).ToUniversalTime() });
        foreach (string type in new[] { "event_created", "invitation" })
            publisher.Publish(new MarshalEvent(Name, type, DemoCalDavCalendars.Work, payload, ownerId, world.Time.GetUtcNow(), Trusted: true));
    }
}
