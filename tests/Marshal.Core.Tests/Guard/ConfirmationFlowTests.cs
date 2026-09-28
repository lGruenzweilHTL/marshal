using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;
using Marshal.Core.Guard;
using Marshal.Core.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Core.Tests.Guard;

public sealed class ConfirmationFlowTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly InMemoryPendingActionStore _store;
    private readonly FakeTool _unlock = FakeTool.Confirm("ha.unlock").Returns("unlocked");
    private readonly FakeJudge _judge = new();
    private readonly SessionId _session = SessionId.New();

    public ConfirmationFlowTests() => _store = new InMemoryPendingActionStore(_time);

    [Fact]
    public async Task Handle_NoPendingAction_NothingToDo()
    {
        ConfirmationFlow flow = await FlowAsync();

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "confirm", Context(), Ct);

        Assert.Equal(ConfirmationOutcomeKind.NoPendingAction, outcome.Kind);
    }

    [Fact]
    public async Task Handle_Keyword_ExecutesFrozenActionUnchangedWithoutJudge()
    {
        ConfirmationFlow flow = await FlowAsync();
        PendingActionRecord frozen = await FreezeAsync("""{"name":"front door","code":1234}""");

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "Bestätigt!", Context("de"), Ct);

        Assert.Equal(ConfirmationOutcomeKind.Executed, outcome.Kind);
        Assert.Equal(GuardDecision.ConfirmedByKeyword, outcome.Decision);
        Assert.Equal("unlocked", outcome.ToolResult?.Value.Content);
        ToolInvocation invocation = Assert.Single(_unlock.Invocations);
        Assert.Equal("""{"name":"front door","code":1234}""", invocation.Arguments.GetRawText());
        Assert.Equal(0, _judge.Calls);
        Assert.Equal((PendingActionStatus.Approved, DecidedBy.Keyword), _store.Decisions[frozen.Id]);
    }

    [Fact]
    public async Task Handle_JudgePositive_Executes()
    {
        ConfirmationFlow flow = await FlowAsync();
        PendingActionRecord frozen = await FreezeAsync("{}");
        _judge.Verdict = JudgeVerdict.Positive;

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "yeah sure, go ahead", Context(), Ct);

        Assert.Equal(ConfirmationOutcomeKind.Executed, outcome.Kind);
        Assert.Equal(GuardDecision.JudgePositive, outcome.Decision);
        Assert.Single(_unlock.Invocations);
        Assert.Equal((PendingActionStatus.Approved, DecidedBy.Judge), _store.Decisions[frozen.Id]);
        Assert.Equal(("unlock the front door", "yeah sure, go ahead"), _judge.LastInput);
    }

    [Fact]
    public async Task Handle_KeywordInsideSentence_GoesToJudge()
    {
        ConfirmationFlow flow = await FlowAsync();
        await FreezeAsync("{}");
        _judge.Verdict = JudgeVerdict.Neutral;

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "bestätigt, aber erst morgen", Context("de"), Ct);

        Assert.Equal(1, _judge.Calls);
        Assert.Equal(ConfirmationOutcomeKind.Dropped, outcome.Kind);
    }

    [Fact]
    public async Task Handle_JudgeNeutral_DropsAndContinuesAsNormalTurn()
    {
        ConfirmationFlow flow = await FlowAsync();
        PendingActionRecord frozen = await FreezeAsync("{}");
        _judge.Verdict = JudgeVerdict.Neutral;

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "what's the weather?", Context(), Ct);

        Assert.Equal(ConfirmationOutcomeKind.Dropped, outcome.Kind);
        Assert.Equal(GuardDecision.JudgeNeutral, outcome.Decision);
        Assert.Empty(_unlock.Invocations);
        Assert.Equal((PendingActionStatus.Dropped, DecidedBy.Judge), _store.Decisions[frozen.Id]);
    }

    [Fact]
    public async Task Handle_JudgeNegative_Cancels()
    {
        ConfirmationFlow flow = await FlowAsync();
        PendingActionRecord frozen = await FreezeAsync("{}");
        _judge.Verdict = JudgeVerdict.Negative;

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "no, leave it", Context(), Ct);

        Assert.Equal(ConfirmationOutcomeKind.Cancelled, outcome.Kind);
        Assert.Equal(GuardDecision.JudgeNegative, outcome.Decision);
        Assert.Empty(_unlock.Invocations);
        Assert.Equal((PendingActionStatus.Rejected, DecidedBy.Judge), _store.Decisions[frozen.Id]);
    }

    [Fact]
    public async Task Handle_JudgeFailed_DropsWithoutExecuting()
    {
        ConfirmationFlow flow = await FlowAsync();
        PendingActionRecord frozen = await FreezeAsync("{}");
        _judge.Verdict = JudgeVerdict.Failed;

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "hmm", Context(), Ct);

        Assert.Equal(ConfirmationOutcomeKind.Dropped, outcome.Kind);
        Assert.Empty(_unlock.Invocations);
        Assert.Equal(PendingActionStatus.Dropped, _store.Decisions[frozen.Id].Status);
    }

    [Fact]
    public async Task Handle_ConfirmAfterExpiry_MarksExpiredWithoutExecuting()
    {
        ConfirmationFlow flow = await FlowAsync();
        PendingActionRecord frozen = await FreezeAsync("{}");
        _time.Advance(TimeSpan.FromSeconds(61));

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "confirm", Context(), Ct);

        Assert.Equal(ConfirmationOutcomeKind.Expired, outcome.Kind);
        Assert.Equal(GuardDecision.Expired, outcome.Decision);
        Assert.Empty(_unlock.Invocations);
        Assert.Equal((PendingActionStatus.Expired, DecidedBy.System), _store.Decisions[frozen.Id]);
    }

    [Fact]
    public async Task Handle_UnrelatedMessageAfterExpiry_ExpiresAndContinuesAsNormalTurn()
    {
        ConfirmationFlow flow = await FlowAsync();
        PendingActionRecord frozen = await FreezeAsync("{}");
        _time.Advance(TimeSpan.FromMinutes(5));
        _judge.Verdict = JudgeVerdict.Neutral;

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "what's the weather?", Context(), Ct);

        Assert.Equal(ConfirmationOutcomeKind.Dropped, outcome.Kind);
        Assert.Equal(PendingActionStatus.Expired, _store.Decisions[frozen.Id].Status);
    }

    [Fact]
    public async Task Handle_ToolNowForbidden_NotExecuted()
    {
        ConfirmationFlow flow = await FlowAsync("""
            risk:
              - match: { tool: "ha.unlock" }
                tier: forbidden
            """);
        await FreezeAsync("{}");

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "confirm", Context(), Ct);

        Assert.Equal(ConfirmationOutcomeKind.Executed, outcome.Kind);
        Assert.Equal("tool.forbidden", outcome.ToolResult?.Error.Code);
        Assert.Empty(_unlock.Invocations);
    }

    [Fact]
    public async Task Handle_ToolRemoved_NotExecuted()
    {
        ConfirmationFlow flow = await FlowAsync();
        await _store.FreezeAsync(new FreezeRequest(_session, null, new ToolId("ha", "gone"), "{}", "x", [], "en", _time.GetUtcNow().AddSeconds(60)), Ct);

        ConfirmationOutcome outcome = await flow.HandleUserTurnAsync(_session, "confirm", Context(), Ct);

        Assert.Equal("tool.unknown", outcome.ToolResult?.Error.Code);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static MarshalExecutionContext Context(string language = "en") => TestExecutionContext.Text(language: language);

    private Task<PendingActionRecord> FreezeAsync(string arguments) => _store.FreezeAsync(
        new FreezeRequest(_session, null, _unlock.Descriptor.Id, arguments, "unlock the front door",
            [ConfirmationReasonText.Rule("unlock the front door", "en")], "en", _time.GetUtcNow().AddSeconds(60)),
        Ct);

    private async Task<ConfirmationFlow> FlowAsync(string rules = "risk: []")
    {
        var registry = new ToolRegistry([new FakeToolSource("test", true, _unlock)]);
        await registry.RefreshAsync(Ct);
        return new ConfirmationFlow(
            _store,
            new ConfiguredConfirmationKeywords(Options.Create(new ConfirmationOptions())),
            _judge,
            registry,
            GuardTestKit.Engine(rules),
            _time);
    }

    private sealed class FakeJudge : IConfirmationJudge
    {
        public JudgeVerdict Verdict { get; set; } = JudgeVerdict.Failed;

        public int Calls { get; private set; }

        public (string Summary, string Reply) LastInput { get; private set; }

        public Task<JudgeVerdict> JudgeAsync(string actionSummary, string userReply, string language, CancellationToken cancellationToken)
        {
            Calls++;
            LastInput = (actionSummary, userReply);
            return Task.FromResult(Verdict);
        }
    }
}
