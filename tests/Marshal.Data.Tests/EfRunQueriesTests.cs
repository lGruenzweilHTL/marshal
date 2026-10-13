using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tools;
using Marshal.Data.Conversations;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class EfRunQueriesTests
{
    private static readonly TimeZoneInfo _vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 27, 21, 30, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task List_FiltersBySourceOutcomeAndCost_NewestFirst()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        RunId voiceOk = await RecordAsync(database, Voice(OwnerA), RunOutcome.Ok, ("fast", 0.10m));
        RunId textError = await RecordAsync(database, Text(OwnerA), RunOutcome.Error, ("standard", 0.50m));
        RunId textOk = await RecordAsync(database, Text(OwnerA), RunOutcome.Ok, ("fast", 0.01m));
        EfRunQueries queries = Queries(database, OwnerA);

        Assert.Equal([textOk, textError, voiceOk], (await queries.ListAsync(new RunFilter(), Ct)).Select(run => run.Id));
        Assert.Equal([voiceOk], (await queries.ListAsync(new RunFilter(Source: SessionSource.Voice), Ct)).Select(run => run.Id));
        Assert.Equal([textError], (await queries.ListAsync(new RunFilter(Outcome: RunOutcome.Error), Ct)).Select(run => run.Id));
        Assert.Equal([textError, voiceOk], (await queries.ListAsync(new RunFilter(MinCost: 0.05m), Ct)).Select(run => run.Id));
        Assert.Equal([textOk], (await queries.ListAsync(new RunFilter(Limit: 1), Ct)).Select(run => run.Id));
    }

    [Fact]
    public async Task List_OtherOwnersRuns_AreNotVisible()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await RecordAsync(database, Text(OwnerB), RunOutcome.Ok);

        Assert.Empty(await Queries(database, OwnerA).ListAsync(new RunFilter(), Ct));
    }

    [Fact]
    public async Task Get_ShowsGuardDecisionReasonsTaintSourcesAndReferenceInsteadOfContent()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        DbRunRecorder recorder = Recorder(database);
        database.Accessor.Current = Text(OwnerA);
        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ToolCall, Tool = new ToolId("mail", "Search"), RiskTier = RiskTier.Safe, GuardDecision = GuardDecision.Safe, Sensitive = true, Content = "Dear Lukas, your password", Reference = "mail:msg-42" }, Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord
        {
            Kind = RunStepKind.ToolCall,
            Tool = new ToolId("memory", "Remember"),
            RiskTier = RiskTier.Confirm,
            GuardDecision = GuardDecision.EscalatedByTaint,
            Reasons = [new ConfirmationReason(ConfirmationReasonCode.Taint, "This run read an email.")],
            Arguments = """{"text":"x"}""",
        }, Ct);
        await recorder.CompleteRunAsync(run, new RunCompletion(RunOutcome.Ok, null, [new TaintSource("mail", 0)]), Ct);

        RunDetail? detail = await Queries(database, OwnerA).GetAsync(run, Ct);

        Assert.NotNull(detail);
        Assert.True(detail.Run.Tainted);
        Assert.Equal([new TaintSource("mail", 0)], detail.TaintSources);
        RunStepDetail mail = detail.Steps[0];
        Assert.Equal(("mail.Search", "mail:msg-42", (string?)null), (mail.Tool, mail.Reference, mail.Content));
        RunStepDetail remember = detail.Steps[1];
        Assert.Equal(GuardDecision.EscalatedByTaint, remember.GuardDecision);
        Assert.Equal(ConfirmationReasonCode.Taint, Assert.Single(remember.Reasons).Code);
        Assert.Equal("""{"text":"x"}""", remember.Arguments);
    }

    [Fact]
    public async Task Get_SubagentSteps_KeepTheirDepth()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        DbRunRecorder recorder = Recorder(database);
        database.Accessor.Current = Text(OwnerA);
        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ModelCall, Model = "m" }, Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ModelCall, Model = "m", Depth = 1 }, Ct);

        RunDetail? detail = await Queries(database, OwnerA).GetAsync(run, Ct);

        Assert.Equal([0, 1], detail!.Steps.Select(step => step.Depth));
    }

    [Fact]
    public async Task Get_ModelCallReasoningAndWithheldMarker()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        DbRunRecorder recorder = Recorder(database);
        database.Accessor.Current = Text(OwnerA);
        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ModelCall, Model = "m", Reasoning = "Check the printer first." }, Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ModelCall, Model = "m", ReasoningWithheld = true }, Ct);

        RunDetail? detail = await Queries(database, OwnerA).GetAsync(run, Ct);

        Assert.Equal([("Check the printer first.", false), (null, true)], detail!.Steps.Select(step => (step.Reasoning, step.ReasoningWithheld)));
    }

    [Fact]
    public async Task Get_ContextNotes_RoundTrip()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        DbRunRecorder recorder = Recorder(database);
        database.Accessor.Current = Text(OwnerA);
        RunId run = await recorder.StartRunAsync(new RunStart(Text(OwnerA), "turn"), Ct);
        ContextNote[] notes = [new("project", "Voron", "centroid", 0.81), new("memory", "Nozzle", "device via vector", 0.4)];
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Context, Content = "prompt", Notes = notes }, Ct);

        RunDetail? detail = await Queries(database, OwnerA).GetAsync(run, Ct);

        Assert.Equal(notes, detail!.Steps.Single().Notes);
    }

    [Fact]
    public async Task Get_AnotherOwnersRun_IsNull()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        RunId run = await RecordAsync(database, Text(OwnerB), RunOutcome.Ok);

        Assert.Null(await Queries(database, OwnerA).GetAsync(run, Ct));
    }

    [Fact]
    public async Task Cost_PerLocalDayAndTier_AdvisorSeparate_MatchesTheAdvisorBudget()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        // 21:30 UTC on the 27th is 23:30 in Vienna (27th); 23:30 UTC is already the 28th there.
        await RecordAsync(database, Text(OwnerA), RunOutcome.Ok, ("fast", 0.10m), ("advisor", 1.00m));
        _time.Advance(TimeSpan.FromHours(2));
        await RecordAsync(database, Text(OwnerA), RunOutcome.Ok, ("standard", 0.30m), ("fast", 0.05m));

        CostReport report = await Queries(database, OwnerA).CostAsync(new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 28), _vienna, Ct);

        Assert.Equal([new DateOnly(2026, 9, 27), new DateOnly(2026, 9, 28)], report.Days.Select(day => day.Date));
        DailyCost first = report.Days[0];
        Assert.Equal((1.10m, 1.00m), (first.Total, first.Advisor));
        Assert.Equal(new Dictionary<string, decimal> { ["fast"] = 0.10m }, first.ByTier);
        Assert.Equal(new Dictionary<string, decimal> { ["fast"] = 0.05m, ["standard"] = 0.30m }, report.Days[1].ByTier);
        Assert.Equal((1.45m, 1.00m), (report.Total, report.Advisor));
        database.Accessor.Current = Text(OwnerA);
        Assert.Equal(report.Advisor, await new EfAdvisorUsage(database.Factory).GetAdvisorCostSinceAsync(new DateTimeOffset(2026, 9, 26, 22, 0, 0, TimeSpan.Zero), Ct));
    }

    [Fact]
    public async Task Cost_OutsideTheRange_IsExcluded()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await RecordAsync(database, Text(OwnerA), RunOutcome.Ok, ("fast", 0.10m));

        CostReport report = await Queries(database, OwnerA).CostAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 26), _vienna, Ct);

        Assert.Empty(report.Days);
        Assert.Equal(0m, report.Total);
    }

    private EfRunQueries Queries(TestDatabase database, Abstractions.Identity.OwnerId owner)
    {
        database.Accessor.Current = Text(owner);
        return new EfRunQueries(database.Factory);
    }

    private DbRunRecorder Recorder(TestDatabase database) =>
        new(database.Factory, [], Options.Create(new RecordingOptions()), _time);

    private async Task<RunId> RecordAsync(TestDatabase database, MarshalExecutionContext context, RunOutcome outcome, params (string Tier, decimal Cost)[] calls)
    {
        database.Accessor.Current = context;
        DbRunRecorder recorder = Recorder(database);
        RunId run = await recorder.StartRunAsync(new RunStart(context, "turn"), Ct);
        foreach ((string tier, decimal cost) in calls)
            await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.ModelCall, Model = "m", Tier = tier, Cost = cost }, Ct);

        await recorder.CompleteRunAsync(run, new RunCompletion(outcome, null, []), Ct);
        _time.Advance(TimeSpan.FromSeconds(1));
        return run;
    }
}
