using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class HomeAssistantCacheTests : IAsyncLifetime
{
    private static readonly HomeAssistantCacheTimings _fast = new()
    {
        ReconnectDelay = TimeSpan.FromMilliseconds(10),
        MaxReconnectDelay = TimeSpan.FromMilliseconds(50),
        CommandTimeout = TimeSpan.FromSeconds(1),
        RegistryRefresh = TimeSpan.FromMinutes(10),
    };

    private FakeHomeAssistant _ha = null!;
    private HomeAssistantCache? _cache;
    private TestServerSockets? _sockets;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _ha = await FakeHomeAssistant.StartAsync();

    [Fact]
    public async Task Start_AuthenticatesAndLoadsRegistriesExposureAndStates()
    {
        HaSnapshot snapshot = await StartReadyAsync();

        Assert.Equal(new Uri("ws://homeassistant.local:8123/api/websocket"), Assert.Single(_sockets!.Uris));
        Assert.Equal("Europe/Vienna", snapshot.TimeZone);
        HaArea hall = snapshot.Areas["hall"];
        Assert.Equal(("Hall", "ground_floor"), (hall.Name, hall.FloorId));
        Assert.Equal(["Flur"], hall.Aliases);
        Assert.Equal(["Obergeschoss"], snapshot.Floors["upper_floor"].Aliases);

        HaEntity bedLight = snapshot.Entities["light.bed_light"];
        Assert.Equal(("light", "bedroom", true), (bedLight.Domain, bedLight.AreaId, bedLight.Exposed));
        Assert.Equal(["Nachtlicht"], bedLight.Aliases);
        Assert.Equal("Bed Light", bedLight.Name);
        Assert.Equal("garage", snapshot.Entities["cover.garage_door"].DeviceClass);
        Assert.Equal(("Hall Door", "hall"), (snapshot.Entities["lock.hall_door"].Name, snapshot.Entities["lock.hall_door"].AreaId));
        Assert.False(snapshot.Entities["lock.front_door"].Exposed);
        Assert.Equal("unlocked", snapshot.States["lock.hall_door"].State);
    }

    [Fact]
    public async Task Start_WrongToken_NotReadyWithError()
    {
        Start(token: "wrong");

        await EventuallyAsync(() => _cache!.LastError is not null);

        Assert.Null(_cache!.Current);
        Assert.Contains("auth", _cache.LastError, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, _ha.Connections);
    }

    [Fact]
    public async Task StateChanged_UpdatesCachedState()
    {
        await StartReadyAsync();

        await _ha.SendStateChangedAsync("light.ceiling_lights", "on", "Ceiling Lights");

        await EventuallyAsync(() => _cache!.Current!.States["light.ceiling_lights"].State == "on");
    }

    [Fact]
    public async Task StateChanged_EntityRemoved_StateDropped()
    {
        await StartReadyAsync();

        await _ha.SendEventAsync("state_changed", new JsonObject { ["entity_id"] = "light.ceiling_lights", ["new_state"] = null });

        await EventuallyAsync(() => !_cache!.Current!.States.ContainsKey("light.ceiling_lights"));
    }

    [Fact]
    public async Task EntityRenamed_RegistryEventReloadsRegistries()
    {
        await StartReadyAsync();
        _ha.RegistryEntry("config/entity_registry/list", "entity_id", "light.kitchen_lights")["name"] = "Kitchen Spots";

        await _ha.SendEventAsync("entity_registry_updated", new JsonObject
        {
            ["action"] = "update",
            ["entity_id"] = "light.kitchen_lights",
            ["changes"] = new JsonObject { ["name"] = null },
        });

        await EventuallyAsync(() => _cache!.Current!.Entities["light.kitchen_lights"].Name == "Kitchen Spots");
    }

    [Fact]
    public async Task Reconnect_NotReadyUntilRegistriesAndStatesReloaded()
    {
        await StartReadyAsync();
        _ha.SetState("lock.hall_door", "locked");
        _ha.Stalled = true;

        _ha.Drop();

        await EventuallyAsync(() => _ha.Connections == 2);
        Assert.Null(_cache!.Current);
        _ha.Stalled = false;
        await EventuallyAsync(() => _cache!.Current is not null);
        Assert.Equal("locked", _cache!.Current!.States["lock.hall_door"].State);
        Assert.True(_ha.CountCommands("get_states") >= 2);
    }

    [Fact]
    public async Task RegistryRefresh_PicksUpExposureChanges()
    {
        await StartReadyAsync(_fast with { RegistryRefresh = TimeSpan.FromMilliseconds(100) });
        _ha.Responses["homeassistant/expose_entity/list"]!["exposed_entities"]!["lock.front_door"] = new JsonObject { ["conversation"] = true };

        await EventuallyAsync(() => _cache!.Current!.Entities["lock.front_door"].Exposed);
    }

    public async ValueTask DisposeAsync()
    {
        if (_cache is not null)
        {
            await _cache.StopAsync(CancellationToken.None);
            _cache.Dispose();
        }

        await _ha.DisposeAsync();
    }

    private void Start(string token = FakeHomeAssistant.Token, HomeAssistantCacheTimings? timings = null)
    {
        _sockets = new TestServerSockets(_ha.Server);
        var options = new HomeAssistantOptions { Url = new Uri("http://homeassistant.local:8123"), Token = token };
        _cache = new HomeAssistantCache(Options.Create(options), _sockets, timings ?? _fast, TimeProvider.System, NullLogger<HomeAssistantCache>.Instance);
        _ = _cache.StartAsync(Ct);
    }

    private async Task<HaSnapshot> StartReadyAsync(HomeAssistantCacheTimings? timings = null)
    {
        Start(timings: timings);
        await EventuallyAsync(() => _cache!.Current is not null);
        return _cache!.Current!;
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met in time.");
            await Task.Delay(10, Ct);
        }
    }
}
