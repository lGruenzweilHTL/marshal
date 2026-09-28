using Marshal.Abstractions.Conversation;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Core.Agent;
using Marshal.Testing;

namespace Marshal.Core.Tests.Agent;

public sealed class LatencyCapTests
{
    [Fact]
    public async Task VoiceTurn_SlowModel_EndsWithTooLongReplyAndCancelledOutcome()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenHang();

        Task<AgentRunResult> running = harness.Runner.RunAsync(Request(TestExecutionContext.Voice(), TimeSpan.FromSeconds(8), harness), Ct);
        await WaitForModelCallAsync(harness, 1);
        harness.Time.Advance(TimeSpan.FromSeconds(8));
        AgentRunResult result = await running;

        Assert.Equal(RunOutcome.Cancelled, result.Outcome);
        Assert.Equal("latency_cap", result.OutcomeReason);
        Assert.Equal(FixedReplies.LatencyCap("en"), result.Reply);
        RecordedRun run = harness.Recorder.OnlyRun;
        Assert.Equal(RunOutcome.Cancelled, run.Completion?.Outcome);
        Assert.Equal("latency_cap", run.Completion?.Reason);
        Assert.Equal(RunStepKind.Reply, run.Steps[^1].Kind);
        List<TurnEvent> events = await harness.DrainEventsAsync();
        Assert.Contains(events, turnEvent => turnEvent is StatusEvent);
    }

    [Fact]
    public async Task TextTurn_BeforeCap_Completes()
    {
        LoopHarness harness = await LoopHarness.CreateAsync();
        harness.Model.ThenText("fast enough");

        AgentRunResult result = await harness.Runner.RunAsync(Request(TestExecutionContext.Text(), TimeSpan.FromSeconds(60), harness), Ct);

        Assert.Equal(RunOutcome.Ok, result.Outcome);
    }

    [Fact]
    public async Task CapDuringToolRound_NoFurtherToolExecuted()
    {
        var tool = FakeTool.Safe();
        LoopHarness harness = await LoopHarness.CreateAsync(tools: [tool]);
        harness.Model.ThenToolCall("test_safe").ThenHang().ThenToolCall("test_safe");

        Task<AgentRunResult> running = harness.Runner.RunAsync(Request(TestExecutionContext.Text(), TimeSpan.FromSeconds(60), harness), Ct);
        await WaitForModelCallAsync(harness, 2);
        harness.Time.Advance(TimeSpan.FromSeconds(60));
        AgentRunResult result = await running;

        Assert.Equal("latency_cap", result.OutcomeReason);
        Assert.Single(tool.Invocations);
    }

    [Fact]
    public async Task VoiceCapShorterThanTextCap_ByDefault()
    {
        var options = new AgentOptions();

        Assert.Equal(TimeSpan.FromSeconds(8), options.VoiceLatencyCap);
        Assert.Equal(TimeSpan.FromSeconds(60), options.TextLatencyCap);
        await Task.CompletedTask;
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AgentRunRequest Request(MarshalExecutionContext context, TimeSpan cap, LoopHarness harness) =>
        new() { Context = context, UserText = "slow", LatencyCap = cap, Events = harness.Events.Writer };

    private static async Task WaitForModelCallAsync(LoopHarness harness, int count)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (harness.Model.Requests.Count < count && waited.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10, Ct);
    }
}
