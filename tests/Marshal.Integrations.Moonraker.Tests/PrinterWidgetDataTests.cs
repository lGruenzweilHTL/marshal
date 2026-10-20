using System.Text.Json;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tools;
using Marshal.Abstractions.Widgets;
using Marshal.Testing;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class PrinterWidgetDataTests
{
    private readonly FakePrinterStateCache _states = new();

    [Fact]
    public async Task Status_DataFromTheCacheForTheOnlyPrinter()
    {
        _states.States["voron"] = Printing();

        JsonElement? data = await Widget().GetAsync(Request("{}"), TestContext.Current.CancellationToken);

        Assert.NotNull(data);
        Assert.Equal("voron", data.Value.GetProperty("printer").GetString());
        JsonElement state = data.Value.GetProperty("state");
        Assert.Equal("printing", state.GetProperty("printState").GetString());
        Assert.Equal(0.25, state.GetProperty("progress").GetDouble());
        Assert.Equal(245, state.GetProperty("extruder").GetProperty("target").GetDouble());
        Assert.Equal("benchy.gcode", state.GetProperty("filename").GetString());
    }

    [Fact]
    public async Task Offline_StateIsNull()
    {
        JsonElement? data = await Widget().GetAsync(Request("""{"printer":"voron"}"""), TestContext.Current.CancellationToken);

        Assert.Equal(JsonValueKind.Null, data!.Value.GetProperty("state").ValueKind);
    }

    [Fact]
    public async Task UnknownPrinter_NoWidget()
    {
        Assert.Null(await Widget().GetAsync(Request("""{"printer":"prusa"}"""), TestContext.Current.CancellationToken));
    }

    private PrinterWidgetData Widget() => new(Directory(), _states);

    private static ConfiguredPrinterDirectory Directory() =>
        new(Microsoft.Extensions.Options.Options.Create(new PrinterOptions { Printers = [new PrinterConfig { Name = "voron", Url = new Uri("http://voron") }] }));

    private static WidgetRequest Request(string arguments) =>
        new(new ToolId("printer", "status"), JsonDocument.Parse(arguments).RootElement.Clone(), new ToolOutput("Printing."), TestExecutionContext.Text());

    private static PrinterState Printing() => new(
        "voron", "ready", null, "printing", "benchy.gcode", 0.25, 600, 720, 812.5, 30, 120, 1800, null,
        new HeaterState(200, 245, 1), new HeaterState(100, 100, 0.5), DateTimeOffset.UnixEpoch);
}
