using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Marshal.Core.Evals;
using Marshal.Core.Models;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Evals;

public sealed class EvalRunnerTests
{
    private readonly InMemoryEvalCaseStore _cases = new();
    private readonly InMemoryEvalRunStore _runs = new();
    private readonly FakeReplayer _replayer = new();
    private readonly FakeJudge _judge = new();
    private readonly FakeCap _cap = new();
    private readonly FakeExecutionContextAccessor _accessor = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static EvalCase Case(string reply = "Done.") => new()
    {
        Id = EvalCaseId.New(),
        SourceRunId = RunId.New(),
        Expectation = EvalExpectation.Good,
        Input = "turn off the kitchen light",
        ToolCalls = [new EvalToolCall("ha.HassTurnOff", "{}", "ok", null, false)],
        Reply = reply,
        RecordedAt = DateTimeOffset.UnixEpoch,
        CreatedAt = DateTimeOffset.UnixEpoch,
    };

    private EvalRunner Runner() => new(_runs, _cases, _replayer, _judge, _accessor, NullLogger<EvalRunner>.Instance, _cap);

    [Fact]
    public async Task Run_ReplaysEveryCaseInOrderAndStoresTotalsAndCost()
    {
        EvalCase pass = _cases.Add(Case());
        EvalCase changed = _cases.Add(Case("Something else."));
        EvalCase diverged = _cases.Add(Case());
        _replayer.Results[diverged.Id] = new ReplayResult([new ReplayedCall("ha.HassTurnOff", "{\"x\":1}", false)], "Done.", 0.02m, null);
        EvalRunRecord run = await _runs.CreateAsync(new CandidateConfig(new Dictionary<ModelTier, string>()), [pass.Id, changed.Id, diverged.Id], "admin", 0.05m, Ct);

        await Runner().RunAsync(run.Id, TestExecutionContext.OwnerA, Ct);

        EvalRunRecord done = (await _runs.GetAsync(run.Id, Ct))!;
        Assert.Equal((EvalRunStatus.Completed, 1, 1, 1, 0), (done.Status, done.Pass, done.Changed, done.Diverged, done.Failed));
        Assert.Equal(0.04m, done.Cost);
        Assert.Equal([pass.Id, changed.Id, diverged.Id], _replayer.Replayed);
        Assert.Equal([pass.Id, changed.Id, diverged.Id], (await _runs.ResultsAsync(run.Id, Ct)).Select(result => result.CaseId));
        Assert.Empty(_judge.Scored);
    }

    [Fact]
    public async Task Run_WithJudge_ScoresRepliesAndCountsJudgeCost()
    {
        EvalCase changed = _cases.Add(Case("Something else."));
        EvalRunRecord run = await _runs.CreateAsync(new CandidateConfig(new Dictionary<ModelTier, string>(), JudgeModel: "judge"), [changed.Id], "admin", 0, Ct);

        await Runner().RunAsync(run.Id, TestExecutionContext.OwnerA, Ct);

        EvalCaseResult result = Assert.Single(await _runs.ResultsAsync(run.Id, Ct));
        Assert.Equal((EvalOutcome.Pass, 0.9, 0.01m + 0.005m), (result.Outcome, result.JudgeScore!.Value, result.Cost));
    }

    [Fact]
    public async Task Run_DailyCapUsedUp_StopsAsFailed()
    {
        EvalCase first = _cases.Add(Case());
        EvalCase second = _cases.Add(Case());
        _cap.Left = 0;
        EvalRunRecord run = await _runs.CreateAsync(new CandidateConfig(new Dictionary<ModelTier, string>()), [first.Id, second.Id], "admin", 0, Ct);

        await Runner().RunAsync(run.Id, TestExecutionContext.OwnerA, Ct);

        EvalRunRecord stopped = (await _runs.GetAsync(run.Id, Ct))!;
        Assert.Equal(EvalRunStatus.Failed, stopped.Status);
        Assert.Contains("daily cost cap", stopped.Error, StringComparison.Ordinal);
        Assert.Empty(_replayer.Replayed);
    }

    [Fact]
    public async Task Cancel_StopsBeforeTheNextCase()
    {
        EvalCase first = _cases.Add(Case());
        EvalCase second = _cases.Add(Case());
        EvalRunRecord run = await _runs.CreateAsync(new CandidateConfig(new Dictionary<ModelTier, string>()), [first.Id, second.Id], "admin", 0, Ct);
        EvalRunner runner = Runner();
        _replayer.OnReplay = () => runner.CancelAsync(run.Id, Ct);

        await runner.RunAsync(run.Id, TestExecutionContext.OwnerA, Ct);

        Assert.Equal(EvalRunStatus.Cancelled, (await _runs.GetAsync(run.Id, Ct))!.Status);
        Assert.Equal([first.Id], _replayer.Replayed);
    }

    [Fact]
    public async Task Judge_FakeEvaluatorMetricsBecomeScoreAndCost()
    {
        var routing = new ModelRoutingOptions { Models = { ["judge"] = new ModelOptions { Tier = ModelTier.Standard, Provider = "fake", Model = "judge", Price = new ModelPrice { InputPerMTok = 1_000_000m, OutputPerMTok = 0 } } } };
        FakeChatClient client = new FakeChatClient().ThenText("4", new UsageDetails { InputTokenCount = 3, OutputTokenCount = 1 });
        var models = new EvalModels(Options.Create(routing), new Factory(client), new SingleModelRouter(client));
        var judge = new EvaluatorReplyJudge(models, new PriceTableCostCalculator(Options.Create(routing)), [new FakeEvaluator(5), new FakeEvaluator(3)]);

        JudgeScore score = await judge.ScoreAsync(Case(), "The light is off.", "judge", Ct);

        Assert.Equal(0.75, score.Score);
        Assert.Equal(3m, score.Cost);
        Assert.Null((await judge.ScoreAsync(Case(), "x", "missing", Ct)).Score);
    }

    private sealed class FakeReplayer : IEvalReplayer
    {
        public Dictionary<EvalCaseId, ReplayResult> Results { get; } = [];

        public List<EvalCaseId> Replayed { get; } = [];

        public Func<Task>? OnReplay { get; set; }

        public async Task<ReplayResult> ReplayAsync(EvalCase evalCase, CandidateConfig candidate, CancellationToken cancellationToken)
        {
            Replayed.Add(evalCase.Id);
            if (OnReplay is not null)
                await OnReplay();

            return Results.GetValueOrDefault(evalCase.Id) ?? new ReplayResult([new ReplayedCall("ha.HassTurnOff", "{}", true)], "Done.", 0.01m, null);
        }
    }

    private sealed class FakeJudge : IReplyJudge
    {
        public List<string> Scored { get; } = [];

        public Task<JudgeScore> ScoreAsync(EvalCase evalCase, string reply, string judgeModel, CancellationToken cancellationToken)
        {
            Scored.Add(reply);
            return Task.FromResult(new JudgeScore(0.9, 0.005m));
        }
    }

    private sealed class FakeCap : IDailyCostCap
    {
        public decimal Left { get; set; } = 10;

        public Task<decimal> LeftTodayAsync(OwnerId owner, CancellationToken cancellationToken) => Task.FromResult(Left);
    }

    // Asks the judge model once (so its usage is priced) and reports a fixed 1–5 metric.
    private sealed class FakeEvaluator(double value) : IEvaluator
    {
        public IReadOnlyCollection<string> EvaluationMetricNames => ["fake"];

        public async ValueTask<EvaluationResult> EvaluateAsync(
            IEnumerable<ChatMessage> messages, ChatResponse modelResponse, ChatConfiguration? chatConfiguration = null,
            IEnumerable<EvaluationContext>? additionalContext = null, CancellationToken cancellationToken = default)
        {
            if (value > 4)
                await chatConfiguration!.ChatClient.GetResponseAsync("score", cancellationToken: cancellationToken);
            return new EvaluationResult(new NumericMetric("fake", value));
        }
    }

    private sealed class Factory(IChatClient client) : IChatClientFactory
    {
        public IChatClient Create(string key, ModelOptions options) => client;
    }
}
