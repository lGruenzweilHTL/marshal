using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Testing;

namespace Marshal.Core.Tests.Agent;

public sealed class InputRequestRunTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly InputRequestId _id = InputRequestId.New();

    [Fact]
    public async Task BackgroundTaskRun_EndsAwaitingInput()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Model.ThenToolCall("paperless_upload", new { file = "letter.pdf" }).ThenText("never asked");
        MarshalExecutionContext context = TestExecutionContext.Text() with { Source = SessionSource.Task, IsInteractive = false, TaskId = TaskId.New() };

        AgentRunResult result = await harness.Runner.RunAsync(new AgentRunRequest { Context = context, UserText = "file the letter", Events = harness.Events.Writer }, Ct);

        Assert.Equal((RunOutcome.Ok, "awaiting_input", FixedReplies.AwaitingInput("en")), (result.Outcome, result.OutcomeReason, result.Reply));
        Assert.Single(harness.Model.Requests);
    }

    [Fact]
    public async Task VoiceTurn_SendsTheUserToTheWebUi()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Model.ThenToolCall("paperless_upload", new { file = "letter.pdf" }).ThenText("never asked");

        AgentRunResult result = await harness.Runner.RunAsync(new AgentRunRequest { Context = TestExecutionContext.Voice(language: "de"), UserText = "leg den Brief ab", Events = harness.Events.Writer }, Ct);

        Assert.Equal(("awaiting_input", FixedReplies.InputInWebUi("de")), (result.OutcomeReason, result.Reply));
    }

    [Fact]
    public async Task TextTurn_ChatGetsTheRequestAndTheModelAnswers()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Model.ThenToolCall("paperless_upload", new { file = "letter.pdf" }).ThenText("Please answer the question in the form above.");

        AgentRunResult result = await harness.Runner.RunAsync(new AgentRunRequest { Context = TestExecutionContext.Text(), UserText = "file the letter", Events = harness.Events.Writer }, Ct);

        Assert.Equal("Please answer the question in the form above.", result.Reply);
        InputRequestEvent shown = Assert.Single((await harness.DrainEventsAsync()).OfType<InputRequestEvent>());
        Assert.Equal(_id, shown.Id);
    }

    private Task<LoopHarness> HarnessAsync()
    {
        FakeTool upload = FakeTool.Safe("paperless.upload").Returns(invocation =>
        {
            invocation.Context.Control.RequestInput(_id);
            return new ToolOutput("The server needs input from the user.");
        });
        return LoopHarness.CreateAsync(tools: [upload]);
    }
}
