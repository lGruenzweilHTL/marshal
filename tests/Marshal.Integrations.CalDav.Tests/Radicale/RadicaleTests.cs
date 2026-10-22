using Marshal.Abstractions;
using Marshal.Abstractions.Calendar;
using Marshal.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.CalDav.Tests.Radicale;

/// <summary>The client and provider against a real Radicale server (needs Docker).</summary>
public sealed class RadicaleTests(RadicaleServer server) : IClassFixture<RadicaleServer>
{
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    private readonly FakeSecretStore _secrets = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Discover_Create_List_UpdateOccurrence_Sync()
    {
        string name = await server.CreateCalendarAsync(Ct);
        string id = $"caldav:radicale/{name}";
        HttpCalDavClient client = Client();
        CalDavCalendarProvider provider = Provider(client, name);

        // Discover: the new collection is a writable calendar with sync support.
        CalendarInfo calendar = Assert.Single((await provider.ListCalendarsAsync(Ct)).Value);
        Assert.Equal(id, calendar.Id);
        Assert.True(calendar.Capabilities.HasFlag(CalendarCapabilities.UpdateOccurrence));
        CalDavCalendar collection = Assert.Single((await client.DiscoverAsync(Account(), Ct)).Value.Calendars, entry => entry.Name == name);
        Assert.True(collection.SupportsSync);
        CalDavSyncResult initial = (await client.SyncAsync(Account(), collection.Url, null, Ct)).Value;
        Assert.Empty(initial.Changes);

        // Create: a single event and a weekly series.
        var dentist = new DateTimeOffset(2026, 10, 22, 9, 0, 0, TimeSpan.FromHours(2));
        Result created = await provider.CreateEventAsync(id, new CalendarEventData("Dentist", CalendarTime.At(dentist), CalendarTime.At(dentist.AddHours(1))), Ct);
        Assert.True(created.IsSuccess, created.IsFailure ? created.Error.Message : null);
        Assert.True((await client.PutAsync(Account(), new Uri(collection.Url, "weekly.ics"), Ics.Weekly, null, Ct)).IsSuccess);

        // List: the series expands across the DST change on 25 October.
        var from = new DateTimeOffset(2026, 10, 19, 0, 0, 0, TimeSpan.FromHours(2));
        IReadOnlyList<CalendarEvent> events = (await provider.ListEventsAsync(id, from, from.AddDays(28), Vienna, Ct)).Value;
        Assert.Equal(
            [
                ("Standup", new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(2))),
                ("Dentist", dentist),
                ("Standup", new DateTimeOffset(2026, 10, 26, 9, 0, 0, TimeSpan.FromHours(1))),
                ("Standup", new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(1))),
                ("Standup", new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(1))),
            ],
            events.Select(entry => (entry.Summary, entry.Start.DateTime!.Value)));

        // Occurrence update: only the second standup moves.
        CalendarEvent second = events[2];
        var later = new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.FromHours(1));
        Result updated = await provider.UpdateEventAsync(id, second.Id!, second.RecurrenceId, new CalendarEventData("Standup (moved)", CalendarTime.At(later), CalendarTime.At(later.AddHours(1))), CalendarUpdateScope.Occurrence, Ct);
        Assert.True(updated.IsSuccess, updated.IsFailure ? updated.Error.Message : null);
        IReadOnlyList<CalendarEvent> after = (await provider.ListEventsAsync(id, from, from.AddDays(28), Vienna, Ct)).Value;
        Assert.Equal(
            [
                new DateTimeOffset(2026, 10, 19, 9, 0, 0, TimeSpan.FromHours(2)),
                new DateTimeOffset(2026, 10, 26, 14, 0, 0, TimeSpan.FromHours(1)),
                new DateTimeOffset(2026, 11, 2, 9, 0, 0, TimeSpan.FromHours(1)),
                new DateTimeOffset(2026, 11, 9, 9, 0, 0, TimeSpan.FromHours(1)),
            ],
            after.Where(entry => entry.SeriesId == "weekly-standup").Select(entry => entry.Start.DateTime!.Value));

        // Sync: both resources are reported since the initial token, nothing after the new one.
        CalDavSyncResult changes = (await client.SyncAsync(Account(), collection.Url, initial.Token, Ct)).Value;
        Assert.Equal(2, changes.Changes.Count(change => !change.Removed));
        Assert.Empty((await client.SyncAsync(Account(), collection.Url, changes.Token, Ct)).Value.Changes);
    }

    [Fact]
    public async Task WrongPassword_AuthError()
    {
        _secrets.Values["radicale_password"] = "wrong";

        Result<CalDavDiscovery> discovery = await Client().DiscoverAsync(Account(), Ct);

        Assert.Equal("caldav.auth_failed", discovery.Error.Code);
    }

    private HttpCalDavClient Client()
    {
        _secrets.Values.TryAdd("radicale_password", RadicaleServer.Password);
        var services = new ServiceCollection();
        services.AddHttpClient(HttpCalDavClient.HttpClientName);
        return new HttpCalDavClient(services.BuildServiceProvider().GetRequiredService<IHttpClientFactory>(), _secrets);
    }

    private CalDavCalendarProvider Provider(ICalDavClient client, string calendar)
    {
        var options = new CalDavOptions();
        options.Accounts["radicale"] = new CalDavAccountOptions { Url = server.Url, Username = RadicaleServer.Username, PasswordSecret = "radicale_password", Calendars = [calendar] };
        return new CalDavCalendarProvider(Options.Create(options), client, TimeProvider.System, new ListLogger<CalDavCalendarProvider>());
    }

    private CalDavAccount Account() => new("radicale", server.Url, RadicaleServer.Username, "radicale_password");
}
