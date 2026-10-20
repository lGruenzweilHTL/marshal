using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tools;
using Marshal.Data.Conversations;
using Marshal.Data.Evals;
using Marshal.Data.Identity;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests.Evals;

public sealed class EvalCaseTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 17, 8, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rate_LearningOff_StoresRatingWithoutCase()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        RunId run = await TurnAsync(database, OwnerA);

        Result<RatedRun> rated = await Ratings(database).RateAsync(run, RunRating.Down, " wrong light ", Ct);

        Assert.Null(rated.Value.CaseId);
        Run row = await RunAsync(database, run);
        Assert.Equal((RunRating.Down, "wrong light", _time.GetUtcNow()), (row.Rating!.Value, row.RatingNote, row.RatedAt!.Value));
        Assert.Empty(await Cases(database).ListAsync(Ct));
    }

    [Theory]
    [InlineData(RunRating.Up, EvalExpectation.Good)]
    [InlineData(RunRating.Down, EvalExpectation.Bad)]
    public async Task Rate_LearningOn_CreatesCaseWithExpectation(RunRating rating, EvalExpectation expectation)
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await LearnAsync(database, OwnerA);
        RunId run = await TurnAsync(database, OwnerA);

        Result<RatedRun> rated = await Ratings(database).RateAsync(run, rating, "should have used the ceiling light", Ct);

        EvalCase saved = Assert.Single(await Cases(database).ListAsync(Ct));
        Assert.Equal(saved.Id, rated.Value.CaseId);
        Assert.Equal((run, expectation, "should have used the ceiling light"), (saved.SourceRunId, saved.Expectation, saved.Note));
        Assert.Equal("turn off the kitchen light", saved.Input);
        Assert.Equal("Time: 08:00. Memory: the user calls the ceiling light 'the light'.", saved.TurnContext);
        EvalToolCall call = Assert.Single(saved.ToolCalls);
        Assert.Equal(("ha.HassTurnOff", """{"area":"kitchen","name":"light"}""", "Turned off"), (call.Tool, call.Arguments, call.Result));
        Assert.Equal("Done.", saved.Reply);
        Assert.False(saved.Incomplete);
        Assert.Equal([new EvalMessage("user", "hello"), new EvalMessage("assistant", "Hi.")], saved.History);
    }

    [Fact]
    public async Task Rate_AgainAfterCase_UpdatesItsExpectationAndNote()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await LearnAsync(database, OwnerA);
        RunId run = await TurnAsync(database, OwnerA);
        await Ratings(database).RateAsync(run, RunRating.Up, null, Ct);

        Result<RatedRun> again = await Ratings(database).RateAsync(run, RunRating.Down, "no", Ct);

        Assert.Null(again.Value.CaseId);
        EvalCase saved = Assert.Single(await Cases(database).ListAsync(Ct));
        Assert.Equal((EvalExpectation.Bad, "no"), (saved.Expectation, saved.Note));
    }

    [Fact]
    public async Task Rate_NoteTooLongOrOtherOwnersRun_Rejected()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        RunId run = await TurnAsync(database, OwnerB);
        database.Accessor.Current = Text(OwnerA);

        Assert.Equal(ErrorType.NotFound, (await Ratings(database).RateAsync(run, RunRating.Up, null, Ct)).Error.Type);
        database.Accessor.Current = Text(OwnerB);
        Assert.Equal(ErrorType.Validation, (await Ratings(database).RateAsync(run, RunRating.Up, new string('x', 1001), Ct)).Error.Type);
    }

    [Fact]
    public async Task Clear_RemovesRatingKeepsCase()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await LearnAsync(database, OwnerA);
        RunId run = await TurnAsync(database, OwnerA);
        await Ratings(database).RateAsync(run, RunRating.Up, null, Ct);

        Assert.True((await Ratings(database).ClearAsync(run, Ct)).IsSuccess);

        Assert.Null((await RunAsync(database, run)).Rating);
        Assert.Single(await Cases(database).ListAsync(Ct));
    }

    [Fact]
    public async Task SaveRun_Unrated_IsUnratedAndIdempotent()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        RunId run = await TurnAsync(database, OwnerA);

        EvalCase first = (await Cases(database).SaveRunAsync(run, Ct)).Value;
        EvalCase second = (await Cases(database).SaveRunAsync(run, Ct)).Value;

        Assert.Equal(EvalExpectation.Unrated, first.Expectation);
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.Id, await Cases(database).FindByRunAsync(run, Ct));
    }

    [Fact]
    public async Task SaveRun_SensitiveResultOrNoTurnSnapshot_Incomplete()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        RunId sensitive = await TurnAsync(database, OwnerA, tool: new RunStepRecord
        {
            Kind = RunStepKind.ToolCall, Tool = new ToolId("mail", "read"), Arguments = """{"id":"m1"}""", Content = "secret", Reference = "m1", Sensitive = true,
        });
        RunId old = await TurnAsync(database, OwnerA, turnContext: null);

        Assert.True((await Cases(database).SaveRunAsync(sensitive, Ct)).Value.Incomplete);
        Assert.True((await Cases(database).SaveRunAsync(old, Ct)).Value.Incomplete);
    }

    [Fact]
    public async Task Cases_OwnerScopedEditableDeletableAndSurviveRunPurge()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        RunId run = await TurnAsync(database, OwnerA);
        EvalCase saved = (await Cases(database).SaveRunAsync(run, Ct)).Value;

        await using (MarshalDbContext db = database.CreateContext(OwnerA))
        {
            await db.RunSteps.Where(step => step.RunId == run).ExecuteDeleteAsync(Ct);
            await db.Runs.Where(candidate => candidate.Id == run).ExecuteDeleteAsync(Ct);
        }

        Assert.NotNull(await Cases(database).GetAsync(saved.Id, Ct));
        Assert.True((await Cases(database).UpdateNoteAsync(saved.Id, "use the lamp", Ct)).IsSuccess);
        Assert.Equal("use the lamp", (await Cases(database).GetAsync(saved.Id, Ct))!.Note);
        database.Accessor.Current = Text(OwnerB);
        Assert.Null(await Cases(database).GetAsync(saved.Id, Ct));
        Assert.Equal(ErrorType.NotFound, (await Cases(database).DeleteAsync(saved.Id, Ct)).Error.Type);
        database.Accessor.Current = Text(OwnerA);
        Assert.True((await Cases(database).DeleteAsync(saved.Id, Ct)).IsSuccess);
        Assert.Empty(await Cases(database).ListAsync(Ct));
    }

    [Fact]
    public async Task RunQueries_RatingFilterAndCaseLink()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        RunId up = await TurnAsync(database, OwnerA);
        RunId down = await TurnAsync(database, OwnerA);
        await TurnAsync(database, OwnerA);
        await Ratings(database).RateAsync(up, RunRating.Up, null, Ct);
        await Ratings(database).RateAsync(down, RunRating.Down, "wrong", Ct);
        EvalCase saved = (await Cases(database).SaveRunAsync(down, Ct)).Value;
        var queries = new EfRunQueries(database.Factory);

        RunSummary listed = Assert.Single(await queries.ListAsync(new RunFilter { Rating = RunRating.Down }, Ct));

        Assert.Equal((down, RunRating.Down, "wrong"), (listed.Id, listed.Rating!.Value, listed.RatingNote));
        Assert.Equal(saved.Id, (await queries.GetAsync(down, Ct))!.EvalCaseId);
        Assert.Null((await queries.GetAsync(up, Ct))!.EvalCaseId);
    }

    private EfRunRatings Ratings(TestDatabase database) => new(database.Factory, _time);

    private EfEvalCaseStore Cases(TestDatabase database) => new(database.Factory, _time);

    private static async Task<Run> RunAsync(TestDatabase database, RunId run)
    {
        await using MarshalDbContext db = database.CreateContext();
        return await db.Runs.AsNoTracking().SingleAsync(candidate => candidate.Id == run, Ct);
    }

    private static async Task LearnAsync(TestDatabase database, OwnerId owner)
    {
        await using MarshalDbContext db = database.CreateContext(owner);
        db.UserPreferences.Add(new UserPreferences { UserId = owner, Language = "en", TimeZone = "Europe/Vienna", HelpMarshalLearn = true });
        await db.SaveChangesAsync(Ct);
    }

    // A web turn after an earlier "hello" turn in the same session: one tool call and a reply.
    private async Task<RunId> TurnAsync(
        TestDatabase database, OwnerId owner, RunStepRecord? tool = null, string? turnContext = "Time: 08:00. Memory: the user calls the ceiling light 'the light'.")
    {
        MarshalExecutionContext context = Text(owner) with { Source = SessionSource.Web };
        var session = SessionId.New();
        await using (MarshalDbContext db = database.CreateContext(owner))
        {
            db.Sessions.Add(new Session
            {
                Id = session, OwnerId = owner, Source = SessionSource.Web, IdentityEvidence = "{}", ResolvedBy = "cookie", CreatedAt = _time.GetUtcNow(), LastTurnAt = _time.GetUtcNow(),
            });
            await db.SaveChangesAsync(Ct);
        }

        var recorder = new DbRunRecorder(database.Factory, [], Options.Create(new RecordingOptions()), _time);
        _time.Advance(TimeSpan.FromSeconds(10));
        RunId earlier = await recorder.StartRunAsync(new RunStart(context with { SessionId = session }, "turn"), Ct);
        await recorder.RecordStepAsync(earlier, new RunStepRecord { Kind = RunStepKind.Context, Content = "system", Input = "hello", TurnContext = "" }, Ct);
        await recorder.RecordStepAsync(earlier, new RunStepRecord { Kind = RunStepKind.Reply, Content = "Hi." }, Ct);
        await recorder.CompleteRunAsync(earlier, new RunCompletion(RunOutcome.Ok, null, []), Ct);

        _time.Advance(TimeSpan.FromSeconds(10));
        RunId run = await recorder.StartRunAsync(new RunStart(context with { SessionId = session }, "turn"), Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Context, Content = "system", Input = "turn off the kitchen light", TurnContext = turnContext }, Ct);
        await recorder.RecordStepAsync(run, tool ?? new RunStepRecord
        {
            Kind = RunStepKind.ToolCall, Tool = new ToolId("ha", "HassTurnOff"), Arguments = """{"name":"light", "area":"kitchen"}""", Content = "Turned off",
        }, Ct);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Reply, Content = "Done." }, Ct);
        await recorder.CompleteRunAsync(run, new RunCompletion(RunOutcome.Ok, null, []), Ct);
        return run;
    }
}
