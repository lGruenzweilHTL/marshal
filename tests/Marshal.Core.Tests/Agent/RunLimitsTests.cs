using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Core.Agent;
using Marshal.Core.Models;
using Marshal.Testing;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Tests.Agent;

public sealed class RunLimitsTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Usage_ModelCallsCountedOnTheContext()
    {
        LoopHarness harness = await PricedAsync(FakeTool.Safe());
        harness.Model.ThenToolCall("test_safe").ThenText("done", Usage(1000, 500));
        MarshalExecutionContext context = TestExecutionContext.Text();

        AgentRunResult result = await harness.Runner.RunAsync(new AgentRunRequest { Context = context, UserText = "go" }, Ct);

        Assert.Equal(RunOutcome.Ok, result.Outcome);
        Assert.Equal((2, 1500L, 0.0025m), (context.Usage.ModelCalls, context.Usage.Tokens, context.Usage.Cost));
    }

    [Fact]
    public async Task CostLimit_ExceededAfterAModelCall_StopsWithBudgetExceeded()
    {
        var tool = FakeTool.Safe();
        LoopHarness harness = await PricedAsync(tool);
        harness.Model.Then(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "test_safe")])) { Usage = Usage(1_000_000, 0) }).ThenText("never");

        AgentRunResult result = await harness.Runner.RunAsync(Request(new RunLimits(MaxCost: 0.5m)), Ct);

        Assert.Equal((RunOutcome.Error, "budget_exceeded"), (result.Outcome, result.OutcomeReason));
        Assert.Empty(tool.Invocations);
        Assert.Single(harness.Model.Requests);
    }

    [Fact]
    public async Task TokenLimit_Exceeded_StopsWithBudgetExceeded()
    {
        LoopHarness harness = await PricedAsync(FakeTool.Safe());
        harness.Model.Then(new ChatResponse(new ChatMessage(ChatRole.Assistant, [new FunctionCallContent("c1", "test_safe")])) { Usage = Usage(900, 200) }).ThenText("never");

        AgentRunResult result = await harness.Runner.RunAsync(Request(new RunLimits(MaxTokens: 1000)), Ct);

        Assert.Equal("budget_exceeded", result.OutcomeReason);
    }

    [Fact]
    public async Task StepLimit_FromBudget_StopsWithBudgetExceeded()
    {
        var tool = FakeTool.Safe();
        LoopHarness harness = await PricedAsync(tool);
        harness.Model.ThenToolCall("test_safe").ThenToolCall("test_safe").ThenText("never");

        AgentRunResult result = await harness.Runner.RunAsync(Request(new RunLimits(MaxSteps: 2)), Ct);

        Assert.Equal("budget_exceeded", result.OutcomeReason);
        Assert.Single(tool.Invocations);
        Assert.Equal(2, harness.Model.Requests.Count);
    }

    [Fact]
    public async Task WallTime_Exceeded_StopsWithBudgetExceeded()
    {
        LoopHarness harness = await PricedAsync();
        harness.Model.ThenHang();

        Task<AgentRunResult> running = harness.Runner.RunAsync(Request(new RunLimits(MaxDuration: TimeSpan.FromMinutes(10))), Ct);
        while (harness.Model.Requests.Count == 0)
            await Task.Delay(5, Ct);
        harness.Time.Advance(TimeSpan.FromMinutes(10));
        AgentRunResult result = await running;

        Assert.Equal((RunOutcome.Error, "budget_exceeded"), (result.Outcome, result.OutcomeReason));
    }

    [Fact]
    public async Task WithinLimits_Completes()
    {
        LoopHarness harness = await PricedAsync();
        harness.Model.ThenText("done", Usage(100, 10));

        AgentRunResult result = await harness.Runner.RunAsync(Request(new RunLimits(MaxCost: 1m, MaxTokens: 1000, MaxSteps: 1, MaxDuration: TimeSpan.FromMinutes(1))), Ct);

        Assert.Equal(RunOutcome.Ok, result.Outcome);
    }

    private static async Task<LoopHarness> PricedAsync(params FakeTool[] tools)
    {
        LoopHarness harness = await LoopHarness.CreateAsync(tools: tools);
        harness.Routing.Models["standard"] = new ModelOptions { Price = new ModelPrice { InputPerMTok = 1m, OutputPerMTok = 3m } };
        return harness;
    }

    private static AgentRunRequest Request(RunLimits limits) =>
        new() { Context = TestExecutionContext.Text() with { IsInteractive = false }, UserText = "go", Limits = limits };

    private static UsageDetails Usage(long input, long output) => new() { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output };
}
