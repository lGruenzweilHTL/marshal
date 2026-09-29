using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Integrations.HomeAssistant.Notifications;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Integrations.HomeAssistant.Tests.Notifications;

public sealed class HaNotificationTests : IAsyncLifetime
{
    private readonly FixtureCache _cache = new();
    private readonly FakeUserPreferencesStore _preferences = new();
    private FakeHomeAssistant _ha = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _ha = await FakeHomeAssistant.StartAsync();
        _preferences.Stored[OwnerA] = new UserPreferencesRecord("en", "Europe/Vienna", [], new NotificationTargets("person.alice", ["notify.mobile_app_pixel", "notify.mobile_app_ipad"]), null);
        _cache.Current = Snapshot(
            [
                new HaEntity("person.alice", "Alice", [], null, null, null, false),
                new HaEntity("assist_satellite.office", "Office", [], "office", null, null, true),
                new HaEntity("assist_satellite.kitchen", "Kitchen", [], "kitchen", null, null, true),
                new HaEntity("binary_sensor.office_occupancy", "Office occupancy", [], "office", null, "occupancy", false),
                new HaEntity("binary_sensor.kitchen_occupancy", "Kitchen occupancy", [], "kitchen", null, "occupancy", false),
                new HaEntity("binary_sensor.kitchen_door", "Kitchen door", [], "kitchen", null, "door", false),
            ],
            ("person.alice", "home"), ("binary_sensor.office_occupancy", "on"), ("binary_sensor.kitchen_occupancy", "off"), ("binary_sensor.kitchen_door", "on"));
    }

    public async ValueTask DisposeAsync() => await _ha.DisposeAsync();

    [Fact]
    public async Task Presence_HomeWithOfficeOccupied_OfficeSatellite()
    {
        Assert.Equal(new Presence(PresenceState.Home, "assist_satellite.office"), await Where().GetAsync(OwnerA, Ct));
    }

    [Fact]
    public async Task Presence_HomeNoRoomOccupied_NoSatellite()
    {
        SetState("binary_sensor.office_occupancy", "off");

        Assert.Equal(new Presence(PresenceState.Home), await Where().GetAsync(OwnerA, Ct));
    }

    [Theory]
    [InlineData("not_home", PresenceState.Away)]
    [InlineData("work", PresenceState.Away)]
    [InlineData("unavailable", PresenceState.Unknown)]
    public async Task Presence_NotHome(string state, PresenceState expected)
    {
        SetState("person.alice", state);

        Assert.Equal(new Presence(expected), await Where().GetAsync(OwnerA, Ct));
    }

    [Fact]
    public async Task Presence_NoPersonOrNoCache_Unknown()
    {
        Assert.Equal(Presence.Unknown, await Where().GetAsync(OwnerB, Ct));
        _cache.Current = null;
        Assert.Equal(Presence.Unknown, await Where().GetAsync(OwnerA, Ct));
    }

    [Fact]
    public async Task Announce_CallsAssistSatelliteAnnounce()
    {
        Result result = await new HaVoiceAnnounceChannel(Client()).DeliverAsync(new ChannelMessage(Guid.NewGuid(), OwnerA, "Your print is done.", NotificationPriority.Normal, Satellite: "assist_satellite.office"), Ct);

        Assert.True(result.IsSuccess);
        (string path, JsonNode? body) = Assert.Single(_ha.RestRequests);
        Assert.Equal("/api/services/assist_satellite/announce", path);
        Assert.Equal(("assist_satellite.office", "Your print is done."), ((string?)body!["entity_id"], (string?)body["message"]));
    }

    [Fact]
    public async Task Push_EveryTargetWithLinkTagAndActions()
    {
        var message = new ChannelMessage(Guid.NewGuid(), OwnerA, "Approve the reminder?", NotificationPriority.Normal,
            Link: "https://marshal.example.com/approvals", Tag: "approval-1", Actions: [new NotificationAction("MARSHAL_APPROVE_n1", "Approve"), new NotificationAction("MARSHAL_REJECT_n1", "Reject")]);

        Result result = await Push().DeliverAsync(message, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["/api/services/notify/mobile_app_pixel", "/api/services/notify/mobile_app_ipad"], _ha.RestRequests.Select(request => request.Path));
        JsonNode body = _ha.RestRequests.First().Body!;
        Assert.Equal(("Approve the reminder?", "Marshal"), ((string?)body["message"], (string?)body["title"]));
        JsonNode data = body["data"]!;
        Assert.Equal(("https://marshal.example.com/approvals", "https://marshal.example.com/approvals", "approval-1"), ((string?)data["url"], (string?)data["clickAction"], (string?)data["tag"]));
        Assert.Equal(["MARSHAL_APPROVE_n1", "MARSHAL_REJECT_n1"], data["actions"]!.AsArray().Select(action => (string?)action!["action"]));
    }

    [Fact]
    public async Task Push_SilentAndCritical_InterruptionLevels()
    {
        await Push().DeliverAsync(new ChannelMessage(Guid.NewGuid(), OwnerA, "Quiet.", NotificationPriority.Normal, Silent: true), Ct);
        await Push().DeliverAsync(new ChannelMessage(Guid.NewGuid(), OwnerA, "Print failed!", NotificationPriority.Critical), Ct);

        JsonNode silent = _ha.RestRequests.First().Body!["data"]!;
        Assert.Equal(("passive", "low"), ((string?)silent["push"]!["interruption-level"], (string?)silent["importance"]));
        JsonNode critical = _ha.RestRequests.ElementAt(2).Body!["data"]!;
        Assert.Equal(("critical", "alarm_stream", "high", 0), ((string?)critical["push"]!["interruption-level"], (string?)critical["channel"], (string?)critical["priority"], (int?)critical["ttl"]));
    }

    [Fact]
    public async Task Push_NoTargets_Fails()
    {
        Result result = await Push().DeliverAsync(new ChannelMessage(Guid.NewGuid(), OwnerB, "Hello.", NotificationPriority.Normal), Ct);

        Assert.True(result.IsFailure);
        Assert.Empty(_ha.RestRequests);
    }

    private HaPresenceProvider Where() => new(_cache, _preferences);

    private HaPushChannel Push() => new(Client(), _preferences);

    private HomeAssistantClient Client() =>
        new(new TestServerHttp(_ha.Server), Options.Create(new HomeAssistantOptions { Url = new Uri("http://localhost/"), Token = FakeHomeAssistant.Token }));

    private void SetState(string entity, string state) =>
        _cache.Current = _cache.Current! with { States = _cache.Current.States.SetItem(entity, new HaState(entity, state, JsonDocument.Parse("{}").RootElement, DateTimeOffset.UnixEpoch)) };

    private static HaSnapshot Snapshot(HaEntity[] entities, params (string Entity, string State)[] states) => new()
    {
        Areas = ImmutableDictionary<string, HaArea>.Empty,
        Floors = ImmutableDictionary<string, HaFloor>.Empty,
        Devices = ImmutableDictionary<string, HaDevice>.Empty,
        Entities = entities.ToImmutableDictionary(entity => entity.EntityId),
        States = states.ToImmutableDictionary(state => state.Entity, state => new HaState(state.Entity, state.State, JsonDocument.Parse("{}").RootElement, DateTimeOffset.UnixEpoch)),
    };
}
