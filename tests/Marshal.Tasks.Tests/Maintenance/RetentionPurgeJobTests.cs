using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Runs;
using Marshal.Data;
using Marshal.Data.Audit;
using Marshal.Data.Conversations;
using Marshal.Data.Memory;
using Marshal.Tasks.Maintenance;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Maintenance;

public sealed class RetentionPurgeJobTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 3, 30, 0, TimeSpan.Zero));
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Run_PayloadsOlderThan30Days_DeletedStepMetadataStays()
    {
        RunId old = await RunAsync(OwnerA, TimeSpan.FromDays(31));
        RunId recent = await RunAsync(OwnerB, TimeSpan.FromDays(29));

        Result<string> result = await Job().RunAsync(Ct);

        RunStep oldStep = await StepAsync(OwnerA, old);
        Assert.Null(oldStep.Payload);
        Assert.Equal(("fast-model", 120, 1500), (oldStep.Model, oldStep.TokensIn, oldStep.DurationMs));
        Assert.Equal("""{"reply":"Hi"}""", (await StepAsync(OwnerB, recent)).Payload);
        Assert.Equal("Deleted 1 step payloads older than 30 days and 0 runs older than 365 days.", result.Value);
    }

    [Fact]
    public async Task Run_RunsOlderThanAYear_DeletedWithTheirSteps_AuditKeptWithoutTheLink()
    {
        RunId ancient = await RunAsync(OwnerA, TimeSpan.FromDays(366));
        RunId young = await RunAsync(OwnerA, TimeSpan.FromDays(364));
        await using (MarshalDbContext db = _database.CreateContext(OwnerA))
        {
            db.AuditEvents.Add(new AuditEvent { Id = Guid.NewGuid(), OwnerId = OwnerA, ActorKind = AuditActorKind.System, RunId = ancient, Action = "printer.pause", CreatedAt = _time.GetUtcNow().AddDays(-366) });
            await db.SaveChangesAsync(Ct);
        }

        await Job().RunAsync(Ct);

        await using MarshalDbContext check = _database.CreateContext(OwnerA);
        Assert.Equal([young], await check.Runs.Select(run => run.Id).ToListAsync(Ct));
        Assert.DoesNotContain(await check.RunSteps.ToListAsync(Ct), step => step.RunId == ancient);
        AuditEvent audit = await check.AuditEvents.SingleAsync(Ct);
        Assert.Equal(("printer.pause", (RunId?)null), (audit.Action, audit.RunId));
    }

    [Fact]
    public async Task Run_MemoryChunksUntouched()
    {
        await using (MarshalDbContext db = _database.CreateContext(OwnerA))
        {
            db.Chunks.Add(new Chunk
            {
                Id = ChunkId.FromHash(new byte[32]),
                OwnerId = OwnerA,
                Scope = ChunkScope.General,
                Type = ChunkType.Fact,
                Title = "Old fact",
                Content = "Written two years ago.",
                Importance = 3,
                CreatedBy = ChunkAuthor.User,
                CreatedAt = _time.GetUtcNow().AddYears(-2),
                UpdatedAt = _time.GetUtcNow().AddYears(-2),
                ValidFrom = _time.GetUtcNow().AddYears(-2),
            });
            await db.SaveChangesAsync(Ct);
        }

        await Job().RunAsync(Ct);

        await using MarshalDbContext check = _database.CreateContext(OwnerA);
        Assert.Single(await check.Chunks.ToListAsync(Ct));
    }

    private RetentionPurgeJob Job() => new(_database.Factory, Options.Create(new MaintenanceOptions()), _time);

    private async Task<RunId> RunAsync(OwnerId owner, TimeSpan age)
    {
        var id = RunId.New();
        await using MarshalDbContext db = _database.CreateContext(owner);
        db.Runs.Add(new Run { Id = id, OwnerId = owner, Source = SessionSource.Web, Trigger = "turn", StartedAt = _time.GetUtcNow() - age, Outcome = RunOutcome.Ok });
        db.RunSteps.Add(new RunStep
        {
            Id = Guid.NewGuid(),
            OwnerId = owner,
            RunId = id,
            Sequence = 1,
            Kind = RunStepKind.ModelCall,
            Model = "fast-model",
            TokensIn = 120,
            DurationMs = 1500,
            Payload = """{"reply":"Hi"}""",
        });
        await db.SaveChangesAsync(Ct);
        return id;
    }

    private async Task<RunStep> StepAsync(OwnerId owner, RunId run)
    {
        await using MarshalDbContext db = _database.CreateContext(owner);
        return await db.RunSteps.SingleAsync(step => step.RunId == run, Ct);
    }
}
