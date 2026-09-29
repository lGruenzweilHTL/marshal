using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class PrinterCameraToolsTests
{
    private readonly FakeWebcams _webcams = new();
    private readonly FakeSnapshotSource _snapshots = new();
    private readonly FakeVisionRouter _router = new();
    private readonly FakeTemplateCatalog _templates = new();
    private readonly InMemoryRunRecorder _recorder = new();

    public PrinterCameraToolsTests() =>
        _webcams.Webcams["voron"] = new PrinterWebcam("main", new Uri("http://voron/webcam/?action=snapshot"), null);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Snapshot_ReturnsTheSameOriginImage()
    {
        ToolOutput output = (await InvokeAsync(await ToolAsync("snapshot"), new { }, TestExecutionContext.Text())).Value;

        Assert.Contains("![voron webcam](/api/printers/voron/snapshots/snap1)", output.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Snapshot_HiddenWithoutWebcam()
    {
        _webcams.Webcams.Clear();

        Assert.False(await (await ToolAsync("snapshot")).IsAvailableAsync(TestExecutionContext.Text(), Ct));
        Assert.False(await (await ToolAsync("inspect")).IsAvailableAsync(TestExecutionContext.Text(), Ct));
    }

    [Fact]
    public async Task Inspect_VoiceTurnUsesTheFastVisionModel()
    {
        FakeChatClient fast = new FakeChatClient().ThenText("The print looks fine; layer 30 is going down cleanly.");
        _router.Vision[ModelTier.Fast] = fast;
        _router.Vision[ModelTier.Standard] = new FakeChatClient();

        ToolOutput output = (await InvokeAsync(await ToolAsync("inspect"), new { question = "How does the print look?" }, TestExecutionContext.Voice())).Value;

        Assert.Equal("The print looks fine; layer 30 is going down cleanly.", output.Content);
        ChatMessage user = Assert.Single(Assert.Single(fast.Requests).Messages, message => message.Role == ChatRole.User);
        Assert.Contains("How does the print look?", user.Text, StringComparison.Ordinal);
        DataContent image = Assert.Single(user.Contents.OfType<DataContent>());
        Assert.Equal("image/jpeg", image.MediaType);
        Assert.Equal(_snapshots.Jpeg, image.Data.ToArray());
        Assert.Equal(new ModelRequest(ModelTier.Fast, Vision: true), _router.Requests.Last());
    }

    [Fact]
    public async Task Inspect_FallsBackToStandardVision()
    {
        FakeChatClient standard = new FakeChatClient().ThenText("Spaghetti on the bed.");
        _router.Vision[ModelTier.Standard] = standard;

        ToolOutput output = (await InvokeAsync(await ToolAsync("inspect"), new { question = "Any problems?" }, TestExecutionContext.Voice())).Value;

        Assert.Equal("Spaghetti on the bed.", output.Content);
        Assert.Single(standard.Requests);
    }

    [Fact]
    public async Task Inspect_TemplateTierDecides()
    {
        _templates.Items.Add(FakeTemplateCatalog.Template("printer-watch", tier: ModelTier.Advisor));
        FakeChatClient advisor = new FakeChatClient().ThenText("ok");
        _router.Vision[ModelTier.Advisor] = advisor;
        _router.Vision[ModelTier.Standard] = new FakeChatClient();

        await InvokeAsync(await ToolAsync("inspect"), new { question = "?" }, TestExecutionContext.Text() with { Template = "printer-watch" });

        Assert.Single(advisor.Requests);
    }

    [Fact]
    public async Task Inspect_HiddenWithoutVisionModel()
    {
        Assert.False(await (await ToolAsync("inspect")).IsAvailableAsync(TestExecutionContext.Voice(), Ct));

        _router.Vision[ModelTier.Standard] = new FakeChatClient();
        Assert.True(await (await ToolAsync("inspect")).IsAvailableAsync(TestExecutionContext.Voice(), Ct));
    }

    [Fact]
    public async Task Inspect_RecordsTheModelCall()
    {
        _router.Vision[ModelTier.Standard] = new FakeChatClient().ThenText("fine");
        RunId run = await _recorder.StartRunAsync(new RunStart(TestExecutionContext.Text(), "test"), Ct);

        await InvokeAsync(await ToolAsync("inspect"), new { question = "?" }, TestExecutionContext.Text() with { RunId = run });

        RunStepRecord step = Assert.Single(_recorder.OnlyRun.Steps);
        Assert.Equal((RunStepKind.ModelCall, "standard-vision", "fine"), (step.Kind, step.Model, step.Content));
    }

    [Fact]
    public async Task Inspect_QuestionRequired()
    {
        _router.Vision[ModelTier.Standard] = new FakeChatClient();

        Assert.Equal("printer.invalid_command", (await ToolAsync("inspect")).Validate(JsonSerializer.SerializeToElement(new { })).Error.Code);
    }

    private async Task<ITool> ToolAsync(string name)
    {
        var directory = new ConfiguredPrinterDirectory(Microsoft.Extensions.Options.Options.Create(new PrinterOptions
        {
            Printers = [new PrinterConfig { Name = "voron", Url = new Uri("http://voron") }],
        }));
        var camera = new PrinterCameraTools(directory, _webcams, _snapshots, _router, _templates, _recorder, new ZeroCost(), TimeProvider.System);
        IReadOnlyList<ITool> tools = await new PrinterToolSource(directory, new FakePrinterClient(), new FakePrinterStateCache(), camera).GetToolsAsync(Ct);
        return tools.Single(tool => tool.Descriptor.Id.Name == name);
    }

    private static async Task<Result<ToolOutput>> InvokeAsync(ITool tool, object arguments, MarshalExecutionContext context)
    {
        JsonElement json = JsonSerializer.SerializeToElement(arguments);
        Result valid = tool.Validate(json);
        return valid.IsFailure ? valid.Error : await tool.InvokeAsync(new ToolInvocation(json, context), Ct);
    }

    private sealed class ZeroCost : ICostCalculator
    {
        public decimal Calculate(string modelKey, UsageDetails? usage) => 0;
    }
}
