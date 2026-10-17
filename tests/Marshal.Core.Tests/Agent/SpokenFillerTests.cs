using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Tests.Agent;

public sealed class SpokenFillerTests
{
    private static readonly Dictionary<string, string> _fillers = new() { ["de"] = "Ich schaue mir den Drucker an.", ["en"] = "Let me look at the printer." };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VoiceTurn_SlowTool_FillerIsTheFirstDeltaBeforeTheToolRuns()
    {
        FakeTool snapshot = Slow("printer.snapshot");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: snapshot);
        harness.Latency.Expected[snapshot.Descriptor.Id] = TimeSpan.FromSeconds(2);
        harness.Model.ThenToolCall("printer_snapshot").ThenText("Der Druck sieht gut aus.");

        AgentRunResult result = await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Voice(language: "de")), Ct);

        List<TurnEvent> events = await harness.DrainEventsAsync();
        List<string> deltas = [.. events.OfType<DeltaEvent>().Select(delta => delta.Text)];
        Assert.Equal(["Ich schaue mir den Drucker an.", "Der Druck sieht gut aus."], deltas);
        int filler = events.FindIndex(item => item is DeltaEvent);
        int done = events.FindIndex(item => item is ToolEvent { State: ToolEventState.Ok });
        Assert.True(filler < done);
        Assert.Equal("Ich schaue mir den Drucker an. Der Druck sieht gut aus.", result.Reply);
        Assert.Equal("Ich schaue mir den Drucker an. Der Druck sieht gut aus.", harness.Recorder.OnlyRun.Steps.Last().Content);
    }

    [Fact]
    public async Task VoiceTurn_NoRecordedDurations_FillerSent()
    {
        FakeTool snapshot = Slow("printer.snapshot");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: snapshot);
        harness.Model.ThenToolCall("printer_snapshot").ThenText("Fine.");

        await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Voice()), Ct);

        Assert.Equal("Let me look at the printer.", (await harness.DrainEventsAsync()).OfType<DeltaEvent>().First().Text);
    }

    [Fact]
    public async Task VoiceTurn_FastTool_NoFiller()
    {
        FakeTool snapshot = Slow("printer.snapshot");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: snapshot);
        harness.Latency.Expected[snapshot.Descriptor.Id] = TimeSpan.FromMilliseconds(120);
        harness.Model.ThenToolCall("printer_snapshot").ThenText("Fine.");

        await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Voice()), Ct);

        Assert.Equal(["Fine."], (await harness.DrainEventsAsync()).OfType<DeltaEvent>().Select(delta => delta.Text));
    }

    [Fact]
    public async Task TextTurn_SlowTool_NoFiller()
    {
        FakeTool snapshot = Slow("printer.snapshot");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: snapshot);
        harness.Latency.Expected[snapshot.Descriptor.Id] = TimeSpan.FromSeconds(2);
        harness.Model.ThenToolCall("printer_snapshot").ThenText("Fine.");

        await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Text()), Ct);

        Assert.Equal(["Fine."], (await harness.DrainEventsAsync()).OfType<DeltaEvent>().Select(delta => delta.Text));
    }

    [Fact]
    public async Task VoiceTurn_ModelSpokeFirst_NoFiller()
    {
        FakeTool snapshot = Slow("printer.snapshot");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: snapshot);
        harness.Latency.Expected[snapshot.Descriptor.Id] = TimeSpan.FromSeconds(2);
        harness.Model
            .Then(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new TextContent("One moment."), new FunctionCallContent("call-1", "printer_snapshot")])))
            .ThenText("Fine.");

        await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Voice()), Ct);

        Assert.DoesNotContain("Let me look at the printer.", (await harness.DrainEventsAsync()).OfType<DeltaEvent>().Select(delta => delta.Text));
    }

    [Fact]
    public async Task VoiceTurn_TwoSlowTools_OneFiller()
    {
        FakeTool snapshot = Slow("printer.snapshot");
        FakeTool inspect = Slow("printer.inspect");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [snapshot, inspect]);
        harness.Latency.Expected[snapshot.Descriptor.Id] = TimeSpan.FromSeconds(2);
        harness.Latency.Expected[inspect.Descriptor.Id] = TimeSpan.FromSeconds(2);
        harness.Model.ThenToolCall("printer_snapshot").ThenToolCall("printer_inspect").ThenText("Fine.");

        await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Voice()), Ct);

        Assert.Equal(["Let me look at the printer.", "Fine."], (await harness.DrainEventsAsync()).OfType<DeltaEvent>().Select(delta => delta.Text));
    }

    [Fact]
    public async Task VoiceTurn_CallBecomesPendingAction_NoFiller()
    {
        FakeTool cancel = Slow("printer.cancel", RiskTier.Confirm);
        LoopHarness harness = await LoopHarness.CreateAsync(tools: cancel);
        harness.Latency.Expected[cancel.Descriptor.Id] = TimeSpan.FromSeconds(2);
        harness.Model.ThenToolCall("printer_cancel").ThenText("Cancel the print?");

        await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Voice()), Ct);

        Assert.Equal(["Cancel the print?"], (await harness.DrainEventsAsync()).OfType<DeltaEvent>().Select(delta => delta.Text));
    }

    [Fact]
    public async Task VoiceTurn_NoLineInTurnLanguage_NoFiller()
    {
        FakeTool snapshot = new FakeTool("printer.snapshot").With(descriptor => descriptor with { Fillers = new Dictionary<string, string> { ["de"] = "Moment." } });
        LoopHarness harness = await LoopHarness.CreateAsync(tools: snapshot);
        harness.Latency.Expected[snapshot.Descriptor.Id] = TimeSpan.FromSeconds(2);
        harness.Model.ThenToolCall("printer_snapshot").ThenText("Fine.");

        await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Voice(language: "en")), Ct);

        Assert.Equal(["Fine."], (await harness.DrainEventsAsync()).OfType<DeltaEvent>().Select(delta => delta.Text));
    }

    [Fact]
    public async Task Run_TimeToFirstDelta_RecordedFromRequestStart()
    {
        FakeTool snapshot = Slow("printer.snapshot");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: snapshot);
        long started = harness.Time.GetTimestamp();
        harness.Time.Advance(TimeSpan.FromMilliseconds(900));
        harness.Model.ThenText("Hello.");

        await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Voice()) with { RequestStartedAt = started }, Ct);

        Assert.Equal(900, harness.Recorder.OnlyRun.Completion?.TimeToFirstDeltaMs);
    }

    [Fact]
    public async Task Run_NoDelta_TimeToFirstDeltaNull()
    {
        FakeTool snapshot = Slow("printer.snapshot");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: snapshot);
        harness.Model.ThenText("");

        await harness.Runner.RunAsync(Request(harness, TestExecutionContext.Voice()), Ct);

        Assert.Null(harness.Recorder.OnlyRun.Completion?.TimeToFirstDeltaMs);
    }

    private static FakeTool Slow(string id, RiskTier tier = RiskTier.Safe) =>
        new FakeTool(id, tier).With(descriptor => descriptor with { Fillers = _fillers });

    private static AgentRunRequest Request(LoopHarness harness, MarshalExecutionContext context) => new() { Context = context, UserText = "how is the print?", Events = harness.Events.Writer };
}
