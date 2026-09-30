using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Tools;
using Marshal.Testing;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class PrinterToolsTests
{
    private readonly FakePrinterClient _client = new();
    private readonly FakePrinterStateCache _states = new();
    private readonly FakeBabysitterStatus _babysitter = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tools_FixedTiers()
    {
        IReadOnlyList<ITool> tools = await ToolsAsync(Voron());

        Assert.Equal(
            ["printer.cancel", "printer.edit_queue", "printer.home", "printer.move", "printer.resume", "printer.run_macro", "printer.set_temperature", "printer.start_print"],
            tools.Where(tool => tool.Descriptor.DefaultTier == RiskTier.Confirm).Select(tool => tool.Descriptor.Id.ToString()).Order());
        Assert.Equal(
            ["printer.emergency_stop", "printer.history", "printer.inspect", "printer.job_queue", "printer.list_files", "printer.pause", "printer.snapshot", "printer.status"],
            tools.Where(tool => tool.Descriptor.DefaultTier == RiskTier.Safe).Select(tool => tool.Descriptor.Id.ToString()).Order());
    }

    [Fact]
    public async Task NoPrinterConfigured_NoTools()
    {
        Assert.Empty(await ToolsAsync());
    }

    [Fact]
    public async Task Status_DefaultsToTheOnlyPrinter()
    {
        _states.States["voron"] = Printing();

        ToolOutput output = (await InvokeAsync(Voron(), "status", new { })).Value;

        Assert.Contains("voron", output.Content, StringComparison.Ordinal);
        Assert.Contains("printing benchy.gcode", output.Content, StringComparison.Ordinal);
        Assert.Contains("25%", output.Content, StringComparison.Ordinal);
        Assert.Contains("layer 30/120", output.Content, StringComparison.Ordinal);
        Assert.Contains("hotend 200/245 °C", output.Content, StringComparison.Ordinal);
        Assert.Contains("30 min left", output.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_WatchedPrint_IncludesTheBabysitterAndItsLastVerdicts()
    {
        _states.States["voron"] = Printing();
        _babysitter.Statuses["voron"] = new BabysitterStatus(true,
            [new BabysitterCheck(DateTimeOffset.UnixEpoch, "ok", "looks fine", null), new BabysitterCheck(DateTimeOffset.UnixEpoch, "failure", "spaghetti", null)], null);

        ToolOutput output = (await InvokeAsync(Voron(), "status", new { })).Value;

        Assert.EndsWith("Babysitter: watching this print. Last checks: ok (looks fine), failure (spaghetti).", output.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_NoBabysitter_NotMentioned()
    {
        _states.States["voron"] = Printing();

        ToolOutput output = (await InvokeAsync(Voron(), "status", new { })).Value;

        Assert.DoesNotContain("Babysitter", output.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_SeveralPrintersWithoutName_ErrorListsNames()
    {
        Error error = (await InvokeAsync([Voron(), new PrinterConfig { Name = "prusa", Url = new Uri("http://prusa") }], "status", new { })).Error;

        Assert.Equal("printer.ambiguous", error.Code);
        Assert.Contains("voron, prusa", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Status_Offline_Fails()
    {
        Assert.Equal("printer.offline", (await InvokeAsync(Voron(), "status", new { printer = "voron" })).Error.Code);
    }

    [Fact]
    public async Task Pause_SendsTheCommand()
    {
        Assert.True((await InvokeAsync(Voron(), "pause", new { })).IsSuccess);

        Assert.Equal(("voron", (PrinterCommand)new PrinterCommand.Pause()), Assert.Single(_client.Sent));
    }

    [Fact]
    public async Task ControlTools_MapArgumentsToTypedCommands()
    {
        (string Tool, object Arguments, PrinterCommand Expected)[] calls =
        [
            ("start_print", new { file = "benchy.gcode" }, new PrinterCommand.StartPrint("benchy.gcode")),
            ("resume", new { }, new PrinterCommand.Resume()),
            ("cancel", new { }, new PrinterCommand.Cancel()),
            ("emergency_stop", new { }, new PrinterCommand.EmergencyStop()),
            ("set_temperature", new { heater = "extruder", target = 245 }, new PrinterCommand.SetTemperature(PrinterHeater.Extruder, 245)),
            ("set_temperature", new { heater = "bed", target = 60.5 }, new PrinterCommand.SetTemperature(PrinterHeater.Bed, 60.5)),
            ("move", new { axis = "z", distance = 10 }, new PrinterCommand.Move(PrinterAxis.Z, 10)),
            ("run_macro", new { name = "park" }, new PrinterCommand.RunMacro("park")),
            ("edit_queue", new { action = "clear" }, new PrinterCommand.QueueClear()),
            ("edit_queue", new { action = "pause" }, new PrinterCommand.QueuePause()),
            ("edit_queue", new { action = "start" }, new PrinterCommand.QueueStart()),
            ("edit_queue", new { action = "jump", job_id = "0003" }, new PrinterCommand.QueueJump("0003")),
        ];

        foreach ((string tool, object arguments, _) in calls)
            Assert.True((await InvokeAsync(Voron(), tool, arguments)).IsSuccess, tool);

        Assert.Equal(calls.Select(call => call.Expected), _client.Sent.Select(sent => sent.Command));
    }

    [Fact]
    public async Task ListArguments_Mapped()
    {
        await InvokeAsync(Voron(), "home", new { axes = new[] { "x", "Y" } });
        await InvokeAsync(Voron(), "home", new { });
        await InvokeAsync(Voron(), "edit_queue", new { action = "add", files = new[] { "a.gcode" } });
        await InvokeAsync(Voron(), "edit_queue", new { action = "remove", job_ids = new[] { "0001" } });

        Assert.Equal([PrinterAxis.X, PrinterAxis.Y], Assert.IsType<PrinterCommand.Home>(_client.Sent[0].Command).Axes);
        Assert.Empty(Assert.IsType<PrinterCommand.Home>(_client.Sent[1].Command).Axes);
        Assert.Equal(["a.gcode"], Assert.IsType<PrinterCommand.QueueAdd>(_client.Sent[2].Command).Files);
        Assert.Equal(["0001"], Assert.IsType<PrinterCommand.QueueRemove>(_client.Sent[3].Command).JobIds);
    }

    [Fact]
    public async Task InvalidArguments_FailValidation()
    {
        (string Tool, object Arguments)[] calls =
        [
            ("set_temperature", new { heater = "chamber", target = 40 }),
            ("set_temperature", new { heater = "extruder" }),
            ("move", new { axis = "e", distance = 5 }),
            ("home", new { axes = new[] { "q" } }),
            ("edit_queue", new { action = "delete_file" }),
            ("start_print", new { }),
        ];

        foreach ((string tool, object arguments) in calls)
        {
            ITool found = await ToolAsync(Voron(), tool);
            Assert.Equal("printer.invalid_command", found.Validate(Json(arguments)).Error.Code);
        }
    }

    [Fact]
    public async Task RunMacro_NotAllowlisted_FailsValidationBeforeConfirmation()
    {
        ITool macro = await ToolAsync(Voron(), "run_macro");

        Assert.Equal("printer.macro_not_allowed", macro.Validate(Json(new { name = "FIRMWARE_RESTART" })).Error.Code);
        Assert.True(macro.Validate(Json(new { name = "park" })).IsSuccess);
        Assert.Contains("PARK", macro.Descriptor.ParametersSchema.GetRawText(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunMacro_HiddenWithoutAllowlist()
    {
        ITool macro = await ToolAsync(new PrinterConfig { Name = "voron", Url = new Uri("http://voron") }, "run_macro");

        Assert.False(await macro.IsAvailableAsync(TestExecutionContext.Text(), Ct));
    }

    [Fact]
    public async Task Summaries_NameTheActionAndPrinter()
    {
        ITool temperature = await ToolAsync(Voron(), "set_temperature");

        Assert.Equal("set the hotend to 245 °C on voron", temperature.Summarize(Json(new { heater = "extruder", target = 245 }), "en"));
    }

    [Fact]
    public async Task Reads_Formatted()
    {
        _client.Files = [new PrinterFile("benchy.gcode", DateTimeOffset.FromUnixTimeSeconds(1700000000), 2048)];
        _client.Queue = new JobQueueStatus("ready", [new QueuedJob("0001", "a.gcode", DateTimeOffset.FromUnixTimeSeconds(1700000000))]);
        _client.History = [new PrintJob("0002", "b.gcode", "completed", DateTimeOffset.FromUnixTimeSeconds(1700000000), DateTimeOffset.FromUnixTimeSeconds(1700003600), 3500, 3600, 1000, true)];

        Assert.Contains("benchy.gcode", (await InvokeAsync(Voron(), "list_files", new { })).Value.Content, StringComparison.Ordinal);
        Assert.Contains("0001", (await InvokeAsync(Voron(), "job_queue", new { })).Value.Content, StringComparison.Ordinal);
        Assert.Contains("completed", (await InvokeAsync(Voron(), "history", new { limit = 3 })).Value.Content, StringComparison.Ordinal);
    }

    private static PrinterConfig Voron() => new() { Name = "voron", Url = new Uri("http://voron"), Macros = ["PARK", "LOAD_FILAMENT"] };

    private static PrinterState Printing() => new(
        "voron", "ready", null, "printing", "benchy.gcode", 0.25, 600, 720, 812.5, 30, 120, 1800, null,
        new HeaterState(200, 245, 1), new HeaterState(100, 100, 0.5), DateTimeOffset.UnixEpoch);

    private Task<IReadOnlyList<ITool>> ToolsAsync(params PrinterConfig[] printers)
    {
        var directory = new ConfiguredPrinterDirectory(Microsoft.Extensions.Options.Options.Create(new PrinterOptions { Printers = [.. printers] }));
        var camera = new PrinterCameraTools(directory, new FakeWebcams(), new FakeSnapshotSource(), new FakeVisionRouter(), new FakeTemplateCatalog(), new InMemoryRunRecorder(), new ZeroCost(), TimeProvider.System);
        return new PrinterToolSource(directory, _client, _states, _babysitter, camera).GetToolsAsync(Ct);
    }

    private async Task<ITool> ToolAsync(PrinterConfig printer, string name) =>
        (await ToolsAsync(printer)).Single(tool => tool.Descriptor.Id.Name == name);

    private Task<Result<ToolOutput>> InvokeAsync(PrinterConfig printer, string name, object arguments) => InvokeAsync([printer], name, arguments);

    private async Task<Result<ToolOutput>> InvokeAsync(PrinterConfig[] printers, string name, object arguments)
    {
        ITool tool = (await ToolsAsync(printers)).Single(tool => tool.Descriptor.Id.Name == name);
        JsonElement json = Json(arguments);
        Result valid = tool.Validate(json);
        return valid.IsFailure ? valid.Error : await tool.InvokeAsync(new ToolInvocation(json, TestExecutionContext.Text()), Ct);
    }

    private static JsonElement Json(object arguments) => JsonSerializer.SerializeToElement(arguments);

    private sealed class ZeroCost : Marshal.Abstractions.Models.ICostCalculator
    {
        public decimal Calculate(string modelKey, Microsoft.Extensions.AI.UsageDetails? usage) => 0;
    }
}
