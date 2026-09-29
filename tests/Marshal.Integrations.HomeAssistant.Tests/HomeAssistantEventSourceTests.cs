using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions.Events;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class HomeAssistantEventSourceTests : IAsyncLifetime
{
    private readonly FakeEventPublisher _published = new();
    private FakeHomeAssistant _ha = null!;
    private HomeAssistantEventSource _source = null!;
    private HomeAssistantCache _cache = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _ha = await FakeHomeAssistant.StartAsync();
        _source = new HomeAssistantEventSource(_published, TimeProvider.System);
        var options = new HomeAssistantOptions { Url = new Uri("http://homeassistant.local:8123"), Token = FakeHomeAssistant.Token };
        var timings = new HomeAssistantCacheTimings { ReconnectDelay = TimeSpan.FromMilliseconds(10), MaxReconnectDelay = TimeSpan.FromMilliseconds(50), CommandTimeout = TimeSpan.FromSeconds(1) };
        _cache = new HomeAssistantCache(Options.Create(options), new TestServerSockets(_ha.Server), timings, TimeProvider.System, NullLogger<HomeAssistantCache>.Instance, _source);
        await _cache.StartAsync(Ct);
        await EventuallyAsync(() => _cache.Current is not null);
    }

    public async ValueTask DisposeAsync()
    {
        await _cache.StopAsync(CancellationToken.None);
        _cache.Dispose();
        await _ha.DisposeAsync();
    }

    [Fact]
    public async Task StateChanged_PublishedWithOldAndNewState()
    {
        Assert.True(_source.IsConnected);

        await _ha.SendEventAsync("state_changed", new JsonObject
        {
            ["entity_id"] = "binary_sensor.kitchen_occupancy",
            ["old_state"] = new JsonObject { ["state"] = "off", ["attributes"] = new JsonObject() },
            ["new_state"] = new JsonObject { ["state"] = "on", ["attributes"] = new JsonObject { ["device_class"] = "occupancy" } },
        });

        MarshalEvent published = await NextAsync();
        Assert.Equal(("ha", "state_changed", "binary_sensor.kitchen_occupancy", null), (published.Source, published.Type, published.Entity, published.OwnerId));
        Assert.Equal("off", published.Payload.GetProperty("from").GetString());
        Assert.Equal("on", published.Payload.GetProperty("to").GetString());
        Assert.Equal("occupancy", published.Payload.GetProperty("attributes").GetProperty("device_class").GetString());
    }

    [Fact]
    public async Task StateChanged_AttributesOnly_NotPublished()
    {
        await _ha.SendEventAsync("state_changed", new JsonObject
        {
            ["entity_id"] = "person.alice",
            ["old_state"] = new JsonObject { ["state"] = "home", ["attributes"] = new JsonObject { ["latitude"] = 48.2 } },
            ["new_state"] = new JsonObject { ["state"] = "home", ["attributes"] = new JsonObject { ["latitude"] = 48.3 } },
        });
        await _ha.SendEventAsync("state_changed", new JsonObject
        {
            ["entity_id"] = "light.bed_light",
            ["old_state"] = null,
            ["new_state"] = new JsonObject { ["state"] = "off", ["attributes"] = new JsonObject() },
        });

        MarshalEvent published = await NextAsync();
        Assert.Equal("light.bed_light", published.Entity);
        Assert.Equal(JsonValueKind.Null, published.Payload.GetProperty("from").ValueKind);
        Assert.Empty(_published.Events);
    }

    [Fact]
    public async Task PushAction_PublishedWithActionData()
    {
        await _ha.SendEventAsync("mobile_app_notification_action", new JsonObject
        {
            ["action"] = "MARSHAL_APPROVE_abc123",
            ["tag"] = "approval-1",
        });

        MarshalEvent published = await NextAsync();
        Assert.Equal(("ha", "mobile_app_notification_action", null), (published.Source, published.Type, published.Entity));
        Assert.Equal("MARSHAL_APPROVE_abc123", published.Payload.GetProperty("action").GetString());
        Assert.Equal("approval-1", published.Payload.GetProperty("tag").GetString());
    }

    [Fact]
    public async Task Disconnect_NotConnectedUntilReloaded()
    {
        _ha.FailingCommands["get_states"] = "Not ready.";
        _ha.Drop();

        await EventuallyAsync(() => !_source.IsConnected);
        _ha.FailingCommands.Clear();
        await EventuallyAsync(() => _source.IsConnected);
    }

    private async Task<MarshalEvent> NextAsync()
    {
        await EventuallyAsync(() => !_published.Events.IsEmpty);
        Assert.True(_published.Events.TryDequeue(out MarshalEvent? published));
        return published;
    }

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met in time.");
            await Task.Delay(5, Ct);
        }
    }
}
