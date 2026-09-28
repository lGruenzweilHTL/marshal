using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;
using Marshal.Data.Conversations;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class DbRunRecorderTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly CollectingObserver _observer = new();

    [Fact]
    public async Task Record_TurnWithOneToolCall_StepsInOrderWithFieldsAndCost()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DbRunRecorder recorder = Recorder(database);
        MarshalExecutionContext turn = Voice(OwnerA) with { Language = "de" };

        RunId run = await recorder.StartRunAsync(new RunStart(turn, "turn"), Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Context, Content = "system", Input = "wie warm?" }, Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ModelCall, Model = "claude", Tier = "fast", TokensIn = 100, TokensOut = 20, TokensCached = 50, FirstTokenMs = 120, DurationMs = 300, Cost = 0.25m }, Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ToolCall, Tool = new ToolId("ha", "GetLiveContext"), RiskTier = RiskTier.Safe, GuardDecision = GuardDecision.Safe, Arguments = "{}", Content = "21 °C", DurationMs = 40 }, Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ModelCall, Model = "claude", Tier = "fast", Cost = 0.5m }, Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Reply, Content = "21 Grad.", ContinueConversation = false }, Ct);
        _time.Advance(TimeSpan.FromSeconds(2));
        await recorder.CompleteRunAsync(run, new RunCompletion(RunOutcome.Ok, null, []), Ct);

        Run row = await context.Runs.SingleAsync(Ct);
        List<RunStep> steps = await context.RunSteps.OrderBy(step => step.Sequence).ToListAsync(Ct);
        Assert.Equal([RunStepKind.Context, RunStepKind.ModelCall, RunStepKind.ToolCall, RunStepKind.ModelCall, RunStepKind.Reply], steps.Select(step => step.Kind));
        Assert.Equal([0, 1, 2, 3, 4], steps.Select(step => step.Sequence));
        Assert.All(steps, step => Assert.Equal(OwnerA, step.OwnerId));
        Assert.Equal(0.75m, row.Cost);
        Assert.Equal(RunOutcome.Ok, row.Outcome);
        Assert.Equal(_time.GetUtcNow(), row.EndedAt);
        Assert.Equal(InputMode.Voice, row.InputMode);
        Assert.Equal("assist_satellite.kitchen", row.SatelliteId);
        Assert.Equal("kitchen", row.AreaId);
        Assert.Equal("de", row.Language);
        Assert.Equal("turn", row.Trigger);
        RunStep model = steps[1];
        Assert.Equal(("claude", "fast", 100, 20, 50, 120, 300), (model.Model, model.Tier, model.TokensIn, model.TokensOut, model.TokensCached, model.FirstTokenMs, model.DurationMs));
        RunStep tool = steps[2];
        Assert.Equal(("ha.GetLiveContext", RiskTier.Safe, GuardDecision.Safe), (tool.Tool, tool.RiskTier, tool.GuardDecision));
        Assert.Contains("21 °C", tool.Payload, StringComparison.Ordinal);
        Assert.Contains("wie warm?", steps[0].Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Record_SensitiveToolResult_StoresOnlyReference()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DbRunRecorder recorder = Recorder(database);
        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);

        await recorder.RecordStepAsync(run, new RunStepRecord
        {
            Kind = RunStepKind.ToolCall,
            Tool = new ToolId("mail", "read"),
            Sensitive = true,
            Content = "Dear Lukas, the secret plan is ...",
            Reference = "imap:private/INBOX/4711",
        }, Ct);

        RunStep step = await context.RunSteps.SingleAsync(Ct);
        Assert.DoesNotContain("secret plan", step.Payload, StringComparison.Ordinal);
        Assert.Contains("imap:private/INBOX/4711", step.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("secret plan", _observer.StepContents, StringComparer.Ordinal);
    }

    [Fact]
    public async Task Record_RedactionConfiguredForTool_AppliesWithoutFlag()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DbRunRecorder recorder = Recorder(database, new RecordingOptions { RedactTools = ["notes.*"] });
        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);

        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ToolCall, Tool = new ToolId("notes", "search"), Content = "private note", Reference = "notes/a.md" }, Ct);

        Assert.DoesNotContain("private note", (await context.RunSteps.SingleAsync(Ct)).Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Record_RedactionDisabledForSensitiveTool_KeepsContent()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DbRunRecorder recorder = Recorder(database, new RecordingOptions { KeepContentTools = ["notes.search"] });
        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);

        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ToolCall, Tool = new ToolId("notes", "search"), Sensitive = true, Content = "note text" }, Ct);

        Assert.Contains("note text", (await context.RunSteps.SingleAsync(Ct)).Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Record_LongResult_Truncated()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DbRunRecorder recorder = Recorder(database);
        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);

        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ToolCall, Tool = new ToolId("test", "big"), Content = new string('x', 50_000) }, Ct);

        string payload = (await context.RunSteps.SingleAsync(Ct)).Payload!;
        Assert.True(payload.Length < 20_000);
        Assert.Contains("[truncated]", payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Complete_TaintedRun_StoresSourcesAndReasons()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DbRunRecorder recorder = Recorder(database);
        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord
        {
            Kind = RunStepKind.ToolCall,
            Tool = new ToolId("memory", "remember"),
            GuardDecision = GuardDecision.EscalatedByTaint,
            Reasons = [new ConfirmationReason(ConfirmationReasonCode.Taint, "mail sentence")],
        }, Ct);

        await recorder.CompleteRunAsync(run, new RunCompletion(RunOutcome.Cancelled, "latency_cap", [new TaintSource("mail", 2)]), Ct);

        Run row = await context.Runs.SingleAsync(Ct);
        Assert.True(row.Tainted);
        Assert.Contains("mail", row.TaintSources, StringComparison.Ordinal);
        Assert.Equal("latency_cap", row.OutcomeReason);
        Assert.Equal(RunOutcome.Cancelled, row.Outcome);
        Assert.Contains("mail sentence", (await context.RunSteps.SingleAsync(Ct)).Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Runs_MixedInputModesInOneSession_RecordedPerRun()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DbRunRecorder recorder = Recorder(database);
        var sessionId = SessionId.New();
        context.Sessions.Add(new Session { Id = sessionId, OwnerId = OwnerA, Source = SessionSource.Voice, IdentityEvidence = "{}", ResolvedBy = "owner_default", CreatedAt = _time.GetUtcNow(), LastTurnAt = _time.GetUtcNow() });
        await context.SaveChangesAsync(Ct);

        await recorder.StartRunAsync(new RunStart(Voice(OwnerA) with { SessionId = sessionId }, "turn"), Ct);
        await recorder.StartRunAsync(new RunStart(Text(OwnerA) with { SessionId = sessionId }, "turn"), Ct);

        List<InputMode?> modes = await context.Runs.OrderBy(run => run.StartedAt).Select(run => run.InputMode).ToListAsync(Ct);
        Assert.Equal(new InputMode?[] { InputMode.Voice, InputMode.Text }, modes);
        Assert.Equal(new[] { sessionId, sessionId }, await context.Runs.Select(run => run.SessionId!.Value).ToListAsync(Ct));
    }

    [Fact]
    public async Task Observer_ReceivesSameData()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DbRunRecorder recorder = Recorder(database);

        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ModelCall, Cost = 1m }, Ct);
        await recorder.CompleteRunAsync(run, new RunCompletion(RunOutcome.Ok, null, []), Ct);

        Assert.Equal(["start", "step:ModelCall", "complete:Ok:1"], _observer.Events);
    }

    [Fact]
    public async Task ConversationHistory_RebuiltFromRunsOfSession()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        DbRunRecorder recorder = Recorder(database);
        var sessionId = SessionId.New();
        context.Sessions.Add(new Session { Id = sessionId, OwnerId = OwnerA, Source = SessionSource.Web, IdentityEvidence = "{}", ResolvedBy = "cookie", CreatedAt = _time.GetUtcNow(), LastTurnAt = _time.GetUtcNow() });
        await context.SaveChangesAsync(Ct);
        foreach ((string question, string answer) in new[] { ("q1", "a1"), ("q2", "a2"), ("q3", "a3") })
        {
            RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA) with { SessionId = sessionId }, "turn"), Ct);
            await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Context, Content = "system", Input = question }, Ct);
            await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Reply, Content = answer }, Ct);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        IReadOnlyList<ChatMessage> history = await new EfConversationHistory(database.Factory).LoadAsync(sessionId, maxTurns: 2, Ct);

        Assert.Equal([(ChatRole.User, "q2"), (ChatRole.Assistant, "a2"), (ChatRole.User, "q3"), (ChatRole.Assistant, "a3")], history.Select(message => (message.Role, message.Text)));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DbRunRecorder Recorder(TestDatabase database, RecordingOptions? options = null) =>
        new(database.Factory, [_observer], Options.Create(options ?? new RecordingOptions()), _time);

    private sealed class CollectingObserver : IRunObserver
    {
        public List<string> Events { get; } = [];

        public List<string> StepContents { get; } = [];

        public void RunStarted(RunId runId, RunStart start, DateTimeOffset startedAt) => Events.Add("start");

        public void StepRecorded(RunId runId, int sequence, RunStepRecord step, DateTimeOffset recordedAt)
        {
            Events.Add($"step:{step.Kind}");
            StepContents.Add(step.Content ?? "");
        }

        public void RunCompleted(RunId runId, RunCompletion completion, decimal cost, DateTimeOffset endedAt) =>
            Events.Add($"complete:{completion.Outcome}:{cost.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
    }
}
