using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Core.Agent;
using Marshal.Core.Models;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Agent;

public sealed class AskAdvisorToolTests
{
    private readonly FakeAdvisorUsage _usage = new();

    [Fact]
    public async Task TextTurn_OneAdvisorCallWithoutToolsReturnsAdvice()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Model.ThenToolCall("agent_ask_advisor", new { question = "How should I plan the move?", context = "User moves in May." }).ThenText("Here is a plan.");
        harness.Advisor.ThenText("Split it into three phases.", new UsageDetails { InputTokenCount = 100, OutputTokenCount = 50 });

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text()), Ct);

        FakeChatRequest advisorRequest = Assert.Single(harness.Advisor.Requests);
        Assert.Empty(advisorRequest.ToolNames);
        Assert.Equal(2, advisorRequest.Messages.Count);
        string question = advisorRequest.Messages[1].Text;
        Assert.Contains("How should I plan the move?", question, StringComparison.Ordinal);
        Assert.Contains("User moves in May.", question, StringComparison.Ordinal);
        Assert.DoesNotContain("plan it", question, StringComparison.Ordinal);
        string toolResult = harness.Model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<FunctionResultContent>().Single().Result?.ToString() ?? "";
        Assert.Equal("Split it into three phases.", toolResult);
        RunStepRecord advisorStep = Assert.Single(harness.Recorder.OnlyRun.Steps, step => step.Tier == "advisor");
        Assert.Equal(RunStepKind.ModelCall, advisorStep.Kind);
        Assert.Equal(0.00105m, advisorStep.Cost);
    }

    [Fact]
    public async Task VoiceTurn_AdvisorHidden()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Model.ThenText("ok");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Voice()), Ct);

        Assert.DoesNotContain("agent_ask_advisor", harness.Model.Requests[0].ToolNames);
    }

    [Fact]
    public async Task PerRunLimit_HiddenAfterTwoCallsAndThirdDenied()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Model
            .ThenToolCall("agent_ask_advisor", new { question = "q1", context = "c" })
            .ThenToolCalls(("agent_ask_advisor", new { question = "q2", context = "c" }, "a"), ("agent_ask_advisor", new { question = "q3", context = "c" }, "b"))
            .ThenText("done");
        harness.Advisor.ThenText("a1").ThenText("a2");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text()), Ct);

        Assert.Equal(2, harness.Advisor.Requests.Count);
        Assert.DoesNotContain("agent_ask_advisor", harness.Model.Requests[2].ToolNames);
        Assert.Contains(harness.Recorder.OnlyRun.Steps, step => step.GuardDecision == GuardDecision.Denied && step.Tool?.ToString() == "agent.ask_advisor");
    }

    [Fact]
    public async Task DailyCapReached_Hidden()
    {
        _usage.CostToday = 1m;
        LoopHarness harness = await HarnessAsync();
        harness.Model.ThenText("ok");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text()), Ct);

        Assert.DoesNotContain("agent_ask_advisor", harness.Model.Requests[0].ToolNames);
        Assert.Equal(new DateTimeOffset(2026, 9, 27, 22, 0, 0, TimeSpan.Zero), _usage.LastSince);
    }

    [Fact]
    public async Task NoAdvisorModel_Hidden()
    {
        LoopHarness harness = await HarnessAsync(withAdvisor: false);
        harness.Model.ThenText("ok");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text()), Ct);

        Assert.DoesNotContain("agent_ask_advisor", harness.Model.Requests[0].ToolNames);
    }

    [Fact]
    public async Task BelowCapAndLimits_Visible()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Model.ThenText("ok");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text()), Ct);

        Assert.Contains("agent_ask_advisor", harness.Model.Requests[0].ToolNames);
    }

    [Fact]
    public async Task TemplateMaxAdvisorCallsZero_Hidden()
    {
        LoopHarness harness = await HarnessAsync();
        harness.Templates.Items.Add(FakeTemplateCatalog.Template("cheap", maxAdvisorCalls: 0));
        harness.Model.ThenText("ok");

        await harness.Runner.RunAsync(Request(TestExecutionContext.Text() with { Template = "cheap" }), Ct);

        Assert.DoesNotContain("agent_ask_advisor", harness.Model.Requests[0].ToolNames);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static AgentRunRequest Request(MarshalExecutionContext context) => new() { Context = context, UserText = "plan it" };

    private Task<LoopHarness> HarnessAsync(bool withAdvisor = true) => LoopHarness.CreateAsync(
        withAdvisor: withAdvisor,
        builtIns: harness =>
        [
            new AskAdvisorTool(
                harness.Router,
                harness.Recorder,
                new PriceTableCostCalculator(Options.Create(new ModelRoutingOptions
                {
                    Models = { ["advisor"] = new ModelOptions { Tier = ModelTier.Advisor, Provider = "anthropic", Model = "m", Price = new ModelPrice { InputPerMTok = 3m, OutputPerMTok = 15m } } },
                })),
                _usage,
                new FixedProfiles(),
                harness.Templates,
                Options.Create(harness.Options),
                harness.Time),
        ]);

    private sealed class FakeAdvisorUsage : IAdvisorUsage
    {
        public decimal CostToday { get; set; }

        public DateTimeOffset? LastSince { get; private set; }

        public Task<decimal> GetAdvisorCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
        {
            LastSince = since;
            return Task.FromResult(CostToday);
        }
    }

    private sealed class FixedProfiles : IUserProfileProvider
    {
        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Lukas", "de", "Europe/Vienna"));
    }
}
