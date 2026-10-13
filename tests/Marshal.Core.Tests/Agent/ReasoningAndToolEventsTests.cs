using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Core.Agent;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Tests.Agent;

public sealed class ReasoningAndToolEventsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task TextTurn_ReasoningBeforeDeltas_ToolRunningThenOk_ReasoningKeptInTranscript()
    {
        FakeTool status = FakeTool.Safe("printer.status").Returns("Printing, layer 112 of 268.\nNozzle 240 °C.");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [status]);
        harness.Router.Reasoning = ModelReasoning.Medium;
        harness.Model
            .Then(Reply(new TextReasoningContent("Check the printer first.") { ProtectedData = "sig-1" }, new FunctionCallContent("call-1", "printer_status", new Dictionary<string, object?> { ["printer"] = "voron" })))
            .Then(Reply(new TextReasoningContent("It is printing."), new TextContent("Layer 112 of 268.")));

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text(), harness), Ct);

        List<TurnEvent> events = await harness.DrainEventsAsync();
        Assert.Equal(
            ["reasoning:Check the printer first.", "tool:call-1:printer.status:Running", "tool:call-1:printer.status:Ok", "reasoning:It is printing.", "delta:Layer 112 of 268."],
            events.Select(Describe));
        ToolEvent done = events.OfType<ToolEvent>().Last();
        Assert.Equal(("printer=voron", "Printing, layer 112 of 268."), (done.Arguments, done.Result));
        Assert.NotNull(done.DurationMs);
        Assert.Equal(ReasoningEffort.Medium, harness.Model.Requests[0].Options?.Reasoning?.Effort);
        Assert.Contains(harness.Model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<TextReasoningContent>(), content => content.ProtectedData == "sig-1");
        Assert.Equal(["Check the printer first.", "It is printing."], harness.Recorder.OnlyRun.Steps.Where(step => step.Kind == RunStepKind.ModelCall).Select(step => step.Reasoning));
    }

    [Fact]
    public async Task VoiceTurn_NoReasoningRequested()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Router.Reasoning = ModelReasoning.High;
        harness.Model.ThenText("Done.");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Voice(), harness), Ct);

        Assert.Null(harness.Model.Requests[0].Options?.Reasoning);
    }

    [Fact]
    public async Task UnknownTool_EndsDenied()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenToolCall("door_unlock", new { door = "front" }, "call-9").ThenText("I can't do that.");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text(), harness), Ct);

        Assert.Equal(["tool:call-9:door.unlock:Running", "tool:call-9:door.unlock:Denied"], (await harness.DrainEventsAsync()).OfType<ToolEvent>().Select(Describe));
    }

    [Fact]
    public async Task ConfirmTool_EndsPending()
    {
        FakeTool unlock = FakeTool.Confirm("test.unlock").Returns("unlocked");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [unlock]);
        harness.Model.ThenToolCall("test_unlock", new { door = "front" }, "call-3").ThenText("Should I unlock the front door?");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text(), harness), Ct);

        ToolEvent[] tools = [.. (await harness.DrainEventsAsync()).OfType<ToolEvent>()];
        Assert.Equal(["tool:call-3:test.unlock:Running", "tool:call-3:test.unlock:Pending"], tools.Select(Describe));
        Assert.Null(tools[1].Result);
    }

    [Fact]
    public async Task FailingTool_EndsErrorWithTheCode()
    {
        FakeTool status = FakeTool.Safe("printer.status").Fails("printer.offline", "The printer is offline.");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [status]);
        harness.Model.ThenToolCall("printer_status", null, "call-4").ThenText("It is offline.");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text(), harness), Ct);

        ToolEvent done = (await harness.DrainEventsAsync()).OfType<ToolEvent>().Last();
        Assert.Equal((ToolEventState.Error, "printer.offline"), (done.State, done.Result));
    }

    [Fact]
    public async Task SensitiveTool_NoResultSummary_LaterReasoningWithheld()
    {
        FakeTool read = FakeTool.Sensitive("notes.read").Returns("Door code 1234.");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [read]);
        harness.Router.Reasoning = ModelReasoning.Low;
        harness.Model
            .Then(Reply(new TextReasoningContent("Read the note."), new FunctionCallContent("call-5", "notes_read", new Dictionary<string, object?> { ["path"] = "door.md" })))
            .Then(Reply(new TextReasoningContent("The code is 1234."), new TextContent("I found it.")));

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text(), harness), Ct);

        Assert.Null((await harness.DrainEventsAsync()).OfType<ToolEvent>().Last().Result);
        RunStepRecord[] calls = [.. harness.Recorder.OnlyRun.Steps.Where(step => step.Kind == RunStepKind.ModelCall)];
        Assert.Equal(("Read the note.", false), (calls[0].Reasoning, calls[0].ReasoningWithheld));
        Assert.Equal(((string?)null, true), (calls[1].Reasoning, calls[1].ReasoningWithheld));
    }

    [Fact]
    public async Task Logs_NeverContainReasoning()
    {
        FakeTool status = FakeTool.Safe("printer.status").Fails("printer.offline", "The printer is offline.");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [status]);
        harness.Router.Reasoning = ModelReasoning.High;
        harness.Model
            .Then(Reply(new TextReasoningContent("secret-thought-one"), new FunctionCallContent("call-6", "printer_status", new Dictionary<string, object?>())))
            .Then(Reply(new TextReasoningContent("secret-thought-two"), new FunctionCallContent("call-7", "door_unlock", new Dictionary<string, object?>())))
            .Then(Reply(new TextReasoningContent("secret-thought-three"), new TextContent("It is offline.")));

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text(), harness), Ct);

        Assert.NotEmpty(harness.Logger.Entries);
        Assert.DoesNotContain(harness.Logger.Entries, entry => entry.Contains("secret-thought", StringComparison.Ordinal));
    }

    private static ChatResponse Reply(params AIContent[] contents) => new(new ChatMessage(ChatRole.Assistant, contents));

    private static AgentRunRequest Request(MarshalExecutionContext context, LoopHarness harness) =>
        new() { Context = context, UserText = "how is the printer?", Events = harness.Events.Writer };

    private static string Describe(TurnEvent turnEvent) => turnEvent switch
    {
        ReasoningEvent reasoning => $"reasoning:{reasoning.Text}",
        DeltaEvent delta => $"delta:{delta.Text}",
        ToolEvent tool => $"tool:{tool.CallId}:{tool.Tool}:{tool.State}",
        _ => turnEvent.GetType().Name,
    };
}
