using Marshal.Abstractions.Context;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Context;
using Marshal.Core.Evals;
using Marshal.Core.Guard;
using Marshal.Core.Models;
using Marshal.Core.Tools;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Evals;

public sealed class AgentLoopReplayerTests
{
    private readonly FakeChatClient _live = new();
    private readonly FakeChatClient _candidate = new();
    private readonly FakeTool _turnOff = FakeTool.Safe("ha.HassTurnOff").Returns("LIVE RESULT");
    private readonly FakeTool _remember = FakeTool.Persisting("memory.remember");
    private readonly FakeExecutionContextAccessor _accessor = new(TestExecutionContext.Text());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EvalCase Case(string? turnContext = "Memory then: the light is the ceiling light.") => new()
    {
        Id = EvalCaseId.New(),
        SourceRunId = RunId.New(),
        Expectation = EvalExpectation.Good,
        Input = "turn off the kitchen light",
        History = [new EvalMessage("user", "hello"), new EvalMessage("assistant", "Hi.")],
        TurnContext = turnContext,
        ToolCalls = [new EvalToolCall("ha.HassTurnOff", CanonicalJson.Of("""{"name":"kitchen"}"""), "Turned off the kitchen light", null, false)],
        Reply = "Done.",
        RecordedAt = new DateTimeOffset(2026, 10, 1, 7, 30, 0, TimeSpan.Zero),
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private static readonly CandidateConfig _candidateConfig = new(new Dictionary<ModelTier, string> { [ModelTier.Standard] = "candidate" }, BasePrompt: "You are a test candidate.");

    [Fact]
    public async Task Replay_CandidateModelGetsCaseContextHistoryAndRecordedResult()
    {
        _candidate.ThenToolCall("ha_HassTurnOff", new { name = "kitchen" }).ThenText("Done.");

        ReplayResult result = await (await ReplayerAsync()).ReplayAsync(Case(), _candidateConfig, Ct);

        Assert.Empty(_live.Requests);
        FakeChatRequest first = _candidate.Requests[0];
        Assert.Contains("You are a test candidate.", first.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Memory then", first.SystemPrompt, StringComparison.Ordinal);
        Assert.Contains("Static section", first.SystemPrompt, StringComparison.Ordinal);
        Assert.DoesNotContain("LIVE MEMORY", first.SystemPrompt, StringComparison.Ordinal);
        Assert.Equal(["hello", "Hi.", "turn off the kitchen light"], first.Messages.Where(message => message.Role != ChatRole.System).Select(message => message.Text));
        Assert.Contains(_candidate.Requests[1].Messages, message => message.Contents.OfType<FunctionResultContent>().Any(call => call.Result?.ToString() == "Turned off the kitchen light"));
        Assert.Empty(_turnOff.Invocations);
        Assert.Equal(("Done.", false, null), (result.Reply, result.Diverged, result.Error));
        Assert.Equal([new ReplayedCall("ha.HassTurnOff", """{"name":"kitchen"}""", true)], result.Calls);
    }

    [Fact]
    public async Task Replay_UnrecordedCalls_DivergedAndNothingExecuted()
    {
        _candidate.ThenToolCall("ha_HassTurnOff", new { name = "lamp" }).ThenToolCall("memory_remember", new { text = "likes lamps" }).ThenText("Done.");

        ReplayResult result = await (await ReplayerAsync()).ReplayAsync(Case(), _candidateConfig, Ct);

        Assert.True(result.Diverged);
        Assert.Equal([false, false], result.Calls.Select(call => call.Recorded));
        Assert.Empty(_turnOff.Invocations);
        Assert.Empty(_remember.Invocations);
    }

    [Fact]
    public async Task Replay_TierWithoutCandidate_UsesCurrentRoutingAndTemplate()
    {
        _live.ThenText("Done.");
        var templates = new FakeTemplateCatalog();

        ReplayResult result = await (await ReplayerAsync(templates)).ReplayAsync(Case(), new CandidateConfig(new Dictionary<ModelTier, string>()), Ct);

        Assert.Equal("Done.", result.Reply);
        Assert.Single(_live.Requests);
        Assert.Empty(_candidate.Requests);
    }

    private async Task<AgentLoopReplayer> ReplayerAsync(FakeTemplateCatalog? templates = null)
    {
        templates ??= new FakeTemplateCatalog();
        var registry = new ToolRegistry([new FakeToolSource("test", true, _turnOff, _remember)]);
        await registry.RefreshAsync(Ct);
        RuleBasedRiskEngine engine = GuardTestKit.Engine("risk: []");
        var satellites = new FixedSatellites("assist_satellite.kitchen", SatelliteProfile.Default);
        var visibility = new ToolVisibilityFilter(registry, engine, satellites, templates);
        var routing = new ModelRoutingOptions { Models = { ["candidate"] = new ModelOptions { Tier = ModelTier.Standard, Provider = "fake", Model = "candidate" } } };
        var router = new SingleModelRouter(_live);

        ServiceProvider services = new ServiceCollection()
            .AddSingleton<ITemplateCatalog>(templates)
            .AddSingleton<IContextBuilder>(new ContextBuilder([new BasePromptContributor(), new Section("Static section", ContextStability.Static), new Section("LIVE MEMORY", ContextStability.Turn)]))
            .AddSingleton<IToolVisibilityFilter>(visibility)
            .AddSingleton<Abstractions.Guard.IGuard>(new ToolCallGuard(registry, visibility, engine, satellites))
            .AddSingleton<ICostCalculator>(new PriceTableCostCalculator(Options.Create(routing)))
            .AddSingleton(Options.Create(new AgentOptions()))
            .AddSingleton(Options.Create(new ConfirmationOptions()))
            .AddSingleton<ILogger<AgentRunner>>(NullLogger<AgentRunner>.Instance)
            .BuildServiceProvider();
        var models = new EvalModels(Options.Create(routing), new Factory(_candidate), router);
        return new AgentLoopReplayer(services, models, registry, _accessor);
    }

    private sealed class Section(string text, ContextStability stability) : IContextContributor
    {
        public ContextStability Stability => stability;

        public int Order => 10;

        public Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken) => Task.FromResult<string?>(text);
    }

    private sealed class Factory(IChatClient client) : IChatClientFactory
    {
        public IChatClient Create(string key, ModelOptions options) => client;
    }
}
