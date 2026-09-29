using Marshal.Abstractions.Events;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class MoonrakerEventSourceTests : IAsyncLifetime
{
    private readonly FakeEventPublisher _published = new();
    private FakeMoonraker _fake = null!;
    private MoonrakerStateCache _cache = null!;
    private MoonrakerEventSource _source = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _fake = await FakeMoonraker.StartAsync();
        _cache = new MoonrakerStateCache(
            Microsoft.Extensions.Options.Options.Create(new PrinterOptions { Printers = [new PrinterConfig { Name = "voron", Url = _fake.Server.BaseAddress, ApiKey = FakeMoonraker.ApiKey }] }),
            new TestServerSockets(_fake.Server),
            new MoonrakerTimings { InitialBackoff = TimeSpan.FromMilliseconds(20), MaxBackoff = TimeSpan.FromSeconds(1), RequestTimeout = TimeSpan.FromSeconds(5) },
            TimeProvider.System,
            NullLogger<MoonrakerStateCache>.Instance);
        _source = new MoonrakerEventSource(_cache, _published, TimeProvider.System);
        await _source.StartAsync(Ct);
        await _cache.StartAsync(Ct);
        await EventuallyAsync(() => _source.IsConnected);
    }

    public async ValueTask DisposeAsync()
    {
        await _cache.StopAsync(CancellationToken.None);
        await _source.StopAsync(CancellationToken.None);
        _cache.Dispose();
        await _fake.DisposeAsync();
    }

    [Fact]
    public async Task PrintLifecycle_SemanticEventsInOrder()
    {
        await _fake.PlayAsync("print_start");
        await _fake.PlayAsync("pause");
        await _fake.PlayAsync("resume");
        await _fake.PlayAsync("progress");
        await _fake.PlayAsync("complete");

        await EventuallyAsync(() => Types().Contains("complete"));
        await SettleAsync();
        Assert.Equal(["started", "first_layer_done", "progress", "paused", "resumed", "progress", "progress", "complete"], Types());
        Assert.All(_published.Events, published => Assert.Equal(("printer", "voron", null), (published.Source, published.Entity, published.OwnerId)));
        Assert.Equal([25, 50, 75], _published.Events.Where(published => published.Type == "progress").Select(published => published.Payload.GetProperty("milestone").GetInt32()));
        MarshalEvent complete = _published.Events.Single(published => published.Type == "complete");
        Assert.Equal("benchy.gcode", complete.Payload.GetProperty("filename").GetString());
    }

    [Fact]
    public async Task CancelAndError_Published()
    {
        await _fake.PlayAsync("print_start");
        await _fake.PlayAsync("cancel");
        await _fake.PlayAsync("print_start");
        await _fake.PlayAsync("error");

        await EventuallyAsync(() => Types().Contains("error"));
        Assert.Contains("cancelled", Types());
        MarshalEvent error = _published.Events.Single(published => published.Type == "error");
        Assert.Equal("Heater extruder not heating at expected rate", error.Payload.GetProperty("message").GetString());
    }

    [Fact]
    public async Task TemperatureDiffsOnly_NoEvents()
    {
        await _fake.PlayAsync("temperatures");

        await SettleAsync();
        Assert.Empty(_published.Events);
    }

    [Fact]
    public async Task KlippyShutdown_PublishedOnce()
    {
        await _fake.PlayAsync("shutdown");

        await EventuallyAsync(() => Types().Contains("klippy_shutdown"));
        await SettleAsync();
        Assert.Equal(["klippy_shutdown"], Types());
        Assert.Equal("Emergency stop", _published.Events.Single().Payload.GetProperty("message").GetString());
    }

    [Fact]
    public async Task MissedTransitionAfterReconnect_Published()
    {
        await _fake.PlayAsync("print_start");
        await EventuallyAsync(() => Types().Contains("progress"));
        _fake.Results["printer.objects.subscribe"]!["status"]!["print_stats"]!["state"] = "complete";
        _fake.Results["printer.objects.subscribe"]!["status"]!["print_stats"]!["filename"] = "benchy.gcode";

        _fake.Drop();

        await EventuallyAsync(() => Types().Contains("complete"));
        await SettleAsync();
        Assert.Single(_published.Events, published => published.Type == "complete");
    }

    private List<string> Types() => [.. _published.Events.Select(published => published.Type)];

    private static Task SettleAsync() => Task.Delay(200, Ct);

    private static async Task EventuallyAsync(Func<bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met in time.");
            await Task.Delay(10, Ct);
        }
    }
}
