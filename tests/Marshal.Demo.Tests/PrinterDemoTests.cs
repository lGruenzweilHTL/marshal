using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tools;
using Marshal.Demo.Printers;
using Marshal.Integrations.Moonraker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Marshal.Demo.Tests;

public sealed class PrinterDemoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DemoOff_RealImplementationsStay()
    {
        await using var demo = DemoServices.Create(demo: false);

        Assert.IsType<ConfiguredPrinterDirectory>(demo.Get<IPrinterDirectory>());
        Assert.IsType<MoonrakerPrinterClient>(demo.Get<IPrinterClient>());
        Assert.Empty(demo.Get<IPrinterDirectory>().Printers);
    }

    [Fact]
    public async Task PrintersConfigured_RealImplementationsStay()
    {
        await using var demo = DemoServices.Create(new()
        {
            ["printers:0:name"] = "trident",
            ["printers:0:url"] = "http://trident.local",
        });

        Assert.IsType<ConfiguredPrinterDirectory>(demo.Get<IPrinterDirectory>());
        Assert.IsType<MoonrakerPrinterClient>(demo.Get<IPrinterClient>());
        Assert.DoesNotContain(DemoIntegration.Printers, demo.Get<DemoIntegrations>().SimulatedAtStartup);
    }

    [Fact]
    public async Task NoPrinters_SimulatedAndToolsOffered()
    {
        await using var demo = DemoServices.Create();

        Assert.IsType<DemoPrinterDirectory>(demo.Get<IPrinterDirectory>());
        Assert.IsType<DemoPrinterClient>(demo.Get<IPrinterClient>());
        Assert.IsType<DemoPrinterStateCache>(demo.Get<IPrinterStateCache>());
        Assert.IsType<DemoSnapshotSource>(demo.Get<ISnapshotSource>());
        Assert.IsType<DemoPrinterWebcams>(demo.Get<IPrinterWebcams>());
        Assert.Contains(DemoIntegration.Printers, demo.Get<DemoIntegrations>().SimulatedAtStartup);
        Assert.Contains(demo.Provider.GetServices<IHostedService>(), service => service is DemoPrinterStateCache);
        Dictionary<string, ITool> tools = await demo.ToolsAsync();
        foreach (string id in new[] { "printer.status", "printer.pause", "printer.cancel", "printer.snapshot" })
            Assert.Contains(id, tools.Keys);
    }

    [Fact]
    public async Task Status_ReportsTheRunningJob()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        string status = await DemoServices.InvokeAsync(tools["printer.status"], "{}");

        Assert.Contains("planter_bracket.gcode", status);
        Assert.Contains("printing", status);
    }

    [Fact]
    public async Task Pause_IsSafeAndRunsAtOnce()
    {
        await using var demo = DemoServices.Create();
        ITool pause = (await demo.ToolsAsync())["printer.pause"];

        Assert.Equal(RiskTier.Safe, pause.Descriptor.DefaultTier);
        await DemoServices.InvokeAsync(pause, "{}");

        Assert.Equal("paused", demo.Get<IPrinterStateCache>().Get("voron").Value.PrintState);
    }

    [Fact]
    public async Task Cancel_IsConfirmTierAndCancelsWhenRun()
    {
        await using var demo = DemoServices.Create();
        ITool cancel = (await demo.ToolsAsync())["printer.cancel"];

        Assert.Equal(RiskTier.Confirm, cancel.Descriptor.DefaultTier);
        await DemoServices.InvokeAsync(cancel, "{}");

        Assert.Equal("cancelled", demo.Get<IPrinterStateCache>().Get("voron").Value.PrintState);
    }

    [Fact]
    public async Task Macros_OnlyAllowlistedRun()
    {
        await using var demo = DemoServices.Create();
        IPrinterClient client = demo.Get<IPrinterClient>();

        Result allowed = await client.SendAsync("voron", new PrinterCommand.RunMacro("PARK"), Ct);
        Result denied = await client.SendAsync("voron", new PrinterCommand.RunMacro("FIRMWARE_RESTART"), Ct);

        Assert.True(allowed.IsSuccess);
        Assert.Equal("printer.macro_not_allowed", denied.Error.Code);
    }

    [Fact]
    public async Task Commands_NotifySubscribersAndRaisePrinterEvents()
    {
        await using var demo = DemoServices.Create();
        await StartHostedAsync(demo);
        var seen = new List<PrinterState?>();
        using IDisposable subscription = demo.Get<IPrinterStateCache>().Subscribe((_, state) => seen.Add(state));

        await demo.Get<IPrinterClient>().SendAsync("voron", new PrinterCommand.Pause(), Ct);

        Assert.Contains(seen, state => state?.PrintState == "paused");
        Assert.Contains(demo.Events.Events, published => published is { Source: "printer", Type: "paused", Entity: "voron" });
    }

    [Fact]
    public async Task StateAdvancesWithTheClock()
    {
        await using var demo = DemoServices.Create();
        IPrinterStateCache states = demo.Get<IPrinterStateCache>();
        double before = states.Get("voron").Value.Progress;

        demo.Time.Advance(TimeSpan.FromMinutes(10));

        Assert.True(states.Get("voron").Value.Progress > before);
    }

    [Fact]
    public async Task Snapshot_IsAJpegServedAgainByItsId()
    {
        await using var demo = DemoServices.Create();
        ISnapshotSource snapshots = demo.Get<ISnapshotSource>();

        PrinterSnapshot snapshot = (await snapshots.CaptureAsync("voron", Ct)).Value;
        Result<byte[]> opened = await snapshots.OpenAsync("voron", snapshot.Id, Ct);

        Assert.Equal([0xFF, 0xD8], snapshot.Jpeg[..2]);
        Assert.Equal(snapshot.Jpeg, opened.Value);
        Assert.Equal($"/snapshots/voron/{snapshot.Id}", snapshot.Url);
    }

    [Fact]
    public async Task Webcam_StreamsAJpeg()
    {
        await using var demo = DemoServices.Create();

        await using WebcamFeed feed = (await demo.Get<IPrinterWebcams>().OpenStreamAsync("voron", Ct)).Value;
        using var copy = new MemoryStream();
        await feed.Body.CopyToAsync(copy, Ct);

        Assert.Equal("image/jpeg", feed.ContentType);
        Assert.Equal([0xFF, 0xD8], copy.ToArray()[..2]);
    }

    private static async Task StartHostedAsync(DemoServices demo)
    {
        foreach (IHostedService service in demo.Provider.GetServices<IHostedService>())
        {
            if (service is DemoPrinterStateCache or MoonrakerEventSource)
                await service.StartAsync(Ct);
        }
    }
}
