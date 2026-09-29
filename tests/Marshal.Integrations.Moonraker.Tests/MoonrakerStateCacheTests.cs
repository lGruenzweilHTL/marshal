using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class MoonrakerStateCacheTests : IAsyncLifetime
{
    private FakeMoonraker _fake = null!;
    private MoonrakerStateCache? _cache;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _fake = await FakeMoonraker.StartAsync();

    public async ValueTask DisposeAsync()
    {
        if (_cache is not null)
        {
            await _cache.StopAsync(CancellationToken.None);
            _cache.Dispose();
        }

        await _fake.DisposeAsync();
    }

    [Fact]
    public async Task Start_IdentifiesAsAgentWithTheApiKeyAndSubscribes()
    {
        await StartAsync();

        JsonObject identify = _fake.Requests.First(request => (string?)request["method"] == "server.connection.identify");
        Assert.Equal(("Marshal", "agent", FakeMoonraker.ApiKey), ((string?)identify["params"]!["client_name"], (string?)identify["params"]!["type"], (string?)identify["params"]!["api_key"]));
        Assert.Equal(FakeMoonraker.ApiKey, Assert.Single(_fake.UpgradeKeys));
        JsonObject subscribe = _fake.Requests.First(request => (string?)request["method"] == "printer.objects.subscribe");
        Assert.Equal(
            ["print_stats", "virtual_sdcard", "extruder", "heater_bed", "webhooks", "display_status"],
            subscribe["params"]!["objects"]!.AsObject().Select(entry => entry.Key));
        PrinterState state = State();
        Assert.Equal(("ready", "standby", 25.1, 24.8), (state.KlippyState, state.PrintState, state.Extruder.Temperature, state.Bed.Temperature));
    }

    [Fact]
    public async Task Diff_MergedIntoTheState()
    {
        await StartAsync();

        await _fake.SendAsync(Update(new JsonObject { ["extruder"] = new JsonObject { ["temperature"] = 200.0 } }));

        PrinterState state = await WaitForAsync(candidate => candidate.Extruder.Temperature == 200);
        Assert.Equal((0.0, 24.8, "standby"), (state.Extruder.Target, state.Bed.Temperature, state.PrintState));
    }

    [Fact]
    public async Task PrintStart_ProgressLayersAndEta()
    {
        await StartAsync();

        await _fake.PlayAsync("print_start");

        PrinterState state = await WaitForAsync(candidate => candidate.Progress == 0.25);
        Assert.Equal(("printing", "benchy.gcode", 30, 120), (state.PrintState, state.Filename, state.CurrentLayer, state.TotalLayers));
        Assert.Equal((245.0, 100.0), (state.Extruder.Target, state.Bed.Target));
        Assert.Equal(1800, state.EtaSeconds);
        Assert.Equal(812.5, state.FilamentUsed);
    }

    [Fact]
    public async Task PauseCompleteAndError_JobStates()
    {
        await StartAsync();
        await _fake.PlayAsync("print_start");

        await _fake.PlayAsync("pause");
        Assert.Equal("paused", (await WaitForAsync(candidate => candidate.PrintState == "paused")).PrintState);
        await _fake.PlayAsync("complete");
        PrinterState complete = await WaitForAsync(candidate => candidate.PrintState == "complete");
        Assert.Equal((1.0, (double?)null), (complete.Progress, complete.EtaSeconds));
        await _fake.PlayAsync("error");
        Assert.Equal("Heater extruder not heating at expected rate", (await WaitForAsync(candidate => candidate.PrintState == "error")).Message);
    }

    [Fact]
    public async Task KlippyShutdownThenReady_StateAndResubscription()
    {
        await StartAsync();

        await _fake.PlayAsync("shutdown");
        PrinterState shutdown = await WaitForAsync(candidate => candidate.KlippyState == "shutdown");
        _fake.Results["printer.objects.subscribe"]!["status"]!["webhooks"]!["state"] = "ready";
        _fake.Results["printer.objects.subscribe"]!["status"]!["extruder"]!["temperature"] = 31.0;
        await _fake.PlayAsync("ready");
        PrinterState ready = await WaitForAsync(candidate => candidate.KlippyState == "ready" && candidate.Extruder.Temperature == 31);

        Assert.Equal("Emergency stop", shutdown.StateMessage);
        Assert.Equal(2, _fake.Count("printer.objects.subscribe"));
        Assert.Equal(31, ready.Extruder.Temperature);
    }

    [Fact]
    public async Task Dropped_OfflineThenReconnectsWithAFullReRead()
    {
        await StartAsync(backoff: TimeSpan.FromMilliseconds(400));
        _fake.Results["printer.objects.subscribe"]!["status"]!["extruder"]!["temperature"] = 42.0;

        _fake.Drop();

        Result<PrinterState> offline = await WaitForResultAsync(result => result.IsFailure);
        Assert.Equal("printer.offline", offline.Error.Code);
        PrinterState back = await WaitForAsync(candidate => candidate.Extruder.Temperature == 42);
        Assert.Equal(2, _fake.Connections);
        Assert.Equal(2, _fake.Count("server.connection.identify"));
        Assert.Equal("standby", back.PrintState);
    }

    [Fact]
    public async Task Get_UnknownOrNotYetConnected_Errors()
    {
        _cache = Cache();

        Assert.Equal("printer.offline", _cache.Get("voron").Error.Code);
        Assert.Equal("printer.not_found", _cache.Get("prusa").Error.Code);
    }

    [Fact]
    public async Task Subscribe_NotifiedOfChangesAndOffline()
    {
        await StartAsync(backoff: TimeSpan.FromSeconds(5));
        var seen = new List<(string Printer, PrinterState? State)>();
        using IDisposable subscription = _cache!.Subscribe((printer, state) =>
        {
            lock (seen)
                seen.Add((printer, state));
        });

        await _fake.SendAsync(Update(new JsonObject { ["heater_bed"] = new JsonObject { ["target"] = 60.0 } }));
        await WaitForAsync(candidate => candidate.Bed.Target == 60);
        _fake.Drop();
        await WaitForResultAsync(result => result.IsFailure);

        lock (seen)
        {
            Assert.Contains(seen, change => change.Printer == "voron" && change.State?.Bed.Target == 60);
            Assert.Equal(("voron", (PrinterState?)null), seen[^1]);
        }
    }

    private MoonrakerStateCache Cache(TimeSpan? backoff = null) => new(
        Microsoft.Extensions.Options.Options.Create(new PrinterOptions { Printers = [new PrinterConfig { Name = "voron", Url = _fake.Server.BaseAddress, ApiKey = FakeMoonraker.ApiKey }] }),
        new TestServerSockets(_fake.Server),
        new MoonrakerTimings { InitialBackoff = backoff ?? TimeSpan.FromMilliseconds(20), MaxBackoff = TimeSpan.FromSeconds(1), RequestTimeout = TimeSpan.FromSeconds(5) },
        TimeProvider.System,
        NullLogger<MoonrakerStateCache>.Instance);

    private async Task StartAsync(TimeSpan? backoff = null)
    {
        _cache = Cache(backoff);
        await _cache.StartAsync(Ct);
        await WaitForAsync(_ => true);
    }

    private PrinterState State() => _cache!.Get("voron").Value;

    private async Task<PrinterState> WaitForAsync(Func<PrinterState, bool> condition) =>
        (await WaitForResultAsync(result => result.IsSuccess && condition(result.Value))).Value;

    private async Task<Result<PrinterState>> WaitForResultAsync(Func<Result<PrinterState>, bool> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(5);
        while (true)
        {
            Result<PrinterState> result = _cache!.Get("voron");
            if (condition(result))
                return result;
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"The printer state never matched; last: {(result.IsSuccess ? result.Value.ToString() : result.Error.Code)}");

            await Task.Delay(10, Ct);
        }
    }

    private static JsonObject Update(JsonObject diff) =>
        new() { ["jsonrpc"] = "2.0", ["method"] = "notify_status_update", ["params"] = new JsonArray(diff, 1.0) };
}
