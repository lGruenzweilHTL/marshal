using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Data.Audit;
using Marshal.Data.Conversations;
using Marshal.Data.Demo;
using Marshal.Data.Identity;
using Marshal.Demo.Seeding;
using Marshal.Memory;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Demo.Tests;

public sealed class DemoSeederTests : IAsyncLifetime
{
    private static readonly OwnerId Owner = TestExecutionContext.OwnerA;

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 9, 30, 0, TimeSpan.Zero));
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Seed_WritesEveryPartForTheOwner()
    {
        int recorded = (await Seeder().SeedAsync(Owner, Ct)).Value;

        await using MarshalDbContext db = _database.CreateContext(Owner);
        List<Session> sessions = await db.Sessions.ToListAsync(Ct);
        List<Run> runs = await db.Runs.ToListAsync(Ct);
        Assert.Contains(sessions, session => session.Source == SessionSource.Web);
        Assert.Contains(runs, run => run.InputMode == InputMode.Voice && run.SatelliteId == "assist_satellite.kitchen");
        Assert.Contains(runs, run => run.InputMode == InputMode.Voice && run.SatelliteId == "assist_satellite.office");
        Assert.All(runs, run => Assert.True(db.RunSteps.Any(step => step.RunId == run.Id)));
        Assert.Contains(runs, run => run.TaskId is not null);
        Assert.Contains(await db.AgentTasks.ToListAsync(Ct), task => task.Name == "morning" && task.ResultBody is not null);
        Assert.Contains(await db.Notifications.ToListAsync(Ct), notification => notification.Outcome == NotificationOutcome.Inboxed);
        PendingAction approval = Assert.Single(await db.PendingActions.ToListAsync(Ct));
        Assert.Equal(("printer.cancel", Abstractions.Guard.PendingActionStatus.Pending), (approval.Tool, approval.Status));
        Assert.Equal(AgentTaskStatus.Waiting, (await db.AgentTasks.SingleAsync(task => task.Id == approval.TaskId, Ct)).Status);
        Assert.True(await db.Chunks.CountAsync(Ct) >= 4);
        Assert.True(await db.ChunkEdges.AnyAsync(Ct));
        Assert.Equal(recorded, await db.DemoSeeds.CountAsync(Ct));
    }

    [Fact]
    public async Task Seed_EveryTopLevelRowIsRecorded()
    {
        await Seeder().SeedAsync(Owner, Ct);

        await using MarshalDbContext db = _database.CreateContext(Owner);
        List<DemoSeed> seeds = await db.DemoSeeds.ToListAsync(Ct);
        Assert.Equal((await db.Sessions.Select(row => row.Id).ToListAsync(Ct)).Select(id => id.ToString()).Order(), Recorded(seeds, "sessions"));
        Assert.Equal((await db.Runs.Select(row => row.Id).ToListAsync(Ct)).Select(id => id.ToString()).Order(), Recorded(seeds, "runs"));
        Assert.Equal((await db.AgentTasks.Select(row => row.Id).ToListAsync(Ct)).Select(id => id.ToString()).Order(), Recorded(seeds, "agent_tasks"));
        Assert.Equal((await db.Notifications.Select(row => row.Id).ToListAsync(Ct)).Select(id => id.ToString()).Order(), Recorded(seeds, "notifications"));
        Assert.Equal((await db.PendingActions.Select(row => row.Id).ToListAsync(Ct)).Select(id => id.ToString()).Order(), Recorded(seeds, "pending_actions"));
        Assert.Equal((await db.Chunks.Select(row => row.Id).ToListAsync(Ct)).Select(id => id.ToString()).Order(), Recorded(seeds, "chunks"));
        Assert.All(seeds, seed => Assert.Equal(_time.GetUtcNow(), seed.SeededAt));
    }

    [Fact]
    public async Task Seed_TimesAreRelativeToTheSeedTime()
    {
        await Seeder().SeedAsync(Owner, Ct);

        await using MarshalDbContext db = _database.CreateContext(Owner);
        DateTimeOffset now = _time.GetUtcNow();
        List<Run> runs = await db.Runs.ToListAsync(Ct);
        Assert.All(runs, run => Assert.InRange(run.StartedAt, now.AddDays(-3), now));
        Assert.Contains(runs, run => run.StartedAt < now.AddHours(-12));
        Assert.All(await db.Notifications.ToListAsync(Ct), notification => Assert.InRange(notification.CreatedAt, now.AddDays(-3), now));
        Assert.True((await db.PendingActions.SingleAsync(Ct)).ExpiresAt > now.AddDays(1));
    }

    [Fact]
    public async Task Seed_OtherOwnersSeeNothing()
    {
        await Seeder().SeedAsync(Owner, Ct);

        await using MarshalDbContext db = _database.CreateContext(TestExecutionContext.OwnerB);
        Assert.Empty(await db.Sessions.ToListAsync(Ct));
        Assert.Empty(await db.Runs.ToListAsync(Ct));
        Assert.Empty(await db.DemoSeeds.ToListAsync(Ct));
    }

    [Fact]
    public async Task Seed_AlreadySeeded_Refused()
    {
        DemoSeeder seeder = Seeder();
        await seeder.SeedAsync(Owner, Ct);

        Result<int> again = await seeder.SeedAsync(Owner, Ct);

        Assert.Equal("demo.already_seeded", again.Error.Code);
        Assert.True(await seeder.IsSeededAsync(Owner, Ct));
    }

    [Fact]
    public async Task Seed_WithAllParts_BackgroundResultAndReasoningSeeded()
    {
        await Seeder().SeedAsync(Owner, Ct);

        await using MarshalDbContext db = _database.CreateContext(Owner);
        Assert.Contains(await db.AgentTasks.ToListAsync(Ct), task => task.OriginSessionId is not null && task.ResultBody is not null);
        Assert.Contains(await db.RunSteps.Where(step => step.Kind == RunStepKind.ModelCall).ToListAsync(Ct), step => step.Payload!.Contains("\"reasoning\"", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Seed_WithoutBackgroundResultsAndReasoning_PartsSkipped()
    {
        await Seeder(new DemoSeedParts(BackgroundResults: false, Reasoning: false)).SeedAsync(Owner, Ct);

        await using MarshalDbContext db = _database.CreateContext(Owner);
        Assert.DoesNotContain(await db.AgentTasks.ToListAsync(Ct), task => task.OriginSessionId is not null);
        Assert.DoesNotContain(await db.RunSteps.ToListAsync(Ct), step => step.Payload != null && step.Payload.Contains("\"reasoning\"", StringComparison.Ordinal));
        Assert.True(await db.Runs.AnyAsync(Ct));
    }

    [Fact]
    public async Task Seed_AuditsAsSystem()
    {
        await Seeder().SeedAsync(Owner, Ct);

        await using MarshalDbContext db = _database.CreateContext(Owner);
        AuditEvent audit = Assert.Single(await db.AuditEvents.ToListAsync(Ct));
        Assert.Equal(("demo.seed", AuditActorKind.System), (audit.Action, audit.ActorKind));
    }

    private static IEnumerable<string> Recorded(List<DemoSeed> seeds, string entity) =>
        seeds.Where(seed => seed.Entity == entity).Select(seed => seed.RowId).Order();

    private DemoSeeder Seeder(DemoSeedParts? parts = null) => new(
        _database.Factory,
        _database.Accessor,
        new SqliteMemoryStore(_database.Factory, new FakeEmbeddingGenerator(), _database.Accessor, _time),
        new EfUserProfileProvider(_database.Factory),
        new DbAuditLog(_database.Factory, _database.Accessor, _time),
        Options.Create(new RecordingOptions()),
        _time,
        parts ?? DemoSeedParts.All);
}
