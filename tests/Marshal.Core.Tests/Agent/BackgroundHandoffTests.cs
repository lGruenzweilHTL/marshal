using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Core.Agent;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Tests.Agent;

public sealed class BackgroundHandoffTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VoiceTurn_CapReached_HandedOffWithTheCompletedTranscript()
    {
        FakeTool mail = FakeTool.Untrusted("mail.search", "mail").Returns("Invoice from ACME.");
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [mail]);
        harness.Handoff.Accepts = true;
        harness.Model.ThenToolCall("mail_search", new { query = "invoice" }).ThenHang();

        Task<AgentRunResult> running = harness.Runner.RunAsync(Request(TestExecutionContext.Voice(), TimeSpan.FromSeconds(8), harness), Ct);
        await WaitForModelCallAsync(harness, 2);
        harness.Time.Advance(TimeSpan.FromSeconds(8));
        AgentRunResult result = await running;

        Assert.Equal((RunOutcome.Backgrounded, "latency_cap", FixedReplies.Backgrounded("en")), (result.Outcome, result.OutcomeReason, result.Reply));
        Assert.Equal((RunOutcome.Backgrounded, "latency_cap"), (harness.Recorder.OnlyRun.Completion?.Outcome, harness.Recorder.OnlyRun.Completion?.Reason));
        BackgroundHandoff handoff = Assert.Single(harness.Handoff.HandOffs);
        Assert.Equal(("find the invoice", "latency_cap", "assist_satellite.kitchen"), (handoff.Request, handoff.Reason, handoff.Context.SatelliteId));
        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool], handoff.Transcript.Select(message => message.Role));
        Assert.Contains("Invoice from ACME.", handoff.Transcript[2].Contents.OfType<FunctionResultContent>().Single().Result?.ToString(), StringComparison.Ordinal);
        Assert.Equal(["mail"], handoff.Context.Taint.Sources.Select(source => source.Kind));
        Assert.DoesNotContain(await harness.DrainEventsAsync(), turnEvent => turnEvent is StatusEvent status && status.Text == FixedReplies.LatencyStatus("en"));
    }

    [Fact]
    public async Task CapReached_HandoffRefused_TooLongReplyAsBefore()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenHang();

        Task<AgentRunResult> running = harness.Runner.RunAsync(Request(TestExecutionContext.Voice(), TimeSpan.FromSeconds(8), harness), Ct);
        await WaitForModelCallAsync(harness, 1);
        harness.Time.Advance(TimeSpan.FromSeconds(8));
        AgentRunResult result = await running;

        Assert.Equal((RunOutcome.Cancelled, FixedReplies.LatencyCap("en")), (result.Outcome, result.Reply));
    }

    [Fact]
    public async Task MoveToBackground_EndsTheTurnWithTheStatusMessage()
    {
        FakeTool search = FakeTool.Safe("web.search").Returns("Three results.");
        LoopHarness harness = await LoopHarness.CreateAsync(builtIns: h => [new MoveToBackgroundTool(h.Handoff)], tools: [search]);
        harness.Handoff.Accepts = true;
        harness.Model
            .ThenToolCall("web_search", new { query = "flights" })
            .ThenToolCall("agent_move_to_background", new { statusMessage = "I'll look into it." })
            .ThenText("never asked");

        AgentRunResult result = await harness.Runner.RunAsync(Request(TestExecutionContext.Voice(), null, harness), Ct);

        Assert.Equal((RunOutcome.Backgrounded, "move_to_background", "I'll look into it."), (result.Outcome, result.OutcomeReason, result.Reply));
        Assert.Equal(2, harness.Model.Requests.Count);
        BackgroundHandoff handoff = Assert.Single(harness.Handoff.HandOffs);
        Assert.Equal([ChatRole.User, ChatRole.Assistant, ChatRole.Tool, ChatRole.Assistant, ChatRole.Tool], handoff.Transcript.Select(message => message.Role));
    }

    [Fact]
    public async Task MoveToBackground_VisibleOnlyInInteractiveTopLevelTurns()
    {
        LoopHarness harness = await LoopHarness.CreateAsync(builtIns: h => [new MoveToBackgroundTool(h.Handoff)]);
        var tool = new MoveToBackgroundTool(harness.Handoff);

        Assert.True(await tool.IsAvailableAsync(TestExecutionContext.Voice(), Ct));
        Assert.False(await tool.IsAvailableAsync(TestExecutionContext.Text() with { IsInteractive = false }, Ct));
        Assert.False(await tool.IsAvailableAsync(TestExecutionContext.Text() with { Depth = 1 }, Ct));
        Assert.False(await new MoveToBackgroundTool().IsAvailableAsync(TestExecutionContext.Voice(), Ct));
    }

    [Fact]
    public async Task MoveToBackground_Refused_SaysSo()
    {
        LoopHarness harness = await LoopHarness.CreateAsync(builtIns: h => [new MoveToBackgroundTool(h.Handoff)]);
        harness.Model.ThenToolCall("agent_move_to_background", new { statusMessage = "I'll look into it." });

        AgentRunResult result = await harness.Runner.RunAsync(Request(TestExecutionContext.Text(), null, harness), Ct);

        Assert.Equal((RunOutcome.Error, "background_refused", FixedReplies.BackgroundUnavailable("en")), (result.Outcome, result.OutcomeReason, result.Reply));
    }

    private static AgentRunRequest Request(MarshalExecutionContext context, TimeSpan? cap, LoopHarness harness) =>
        new() { Context = context, UserText = "find the invoice", LatencyCap = cap, Events = harness.Events.Writer };

    private static async Task WaitForModelCallAsync(LoopHarness harness, int count)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (harness.Model.Requests.Count < count && waited.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10, Ct);
    }
}
