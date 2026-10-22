using System.Data.Common;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Data.Audit;
using Marshal.Data.Conversations;
using Marshal.Data.Identity;
using Marshal.Data.Tasks;
using Marshal.Demo.Seeding;
using Marshal.Memory;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Demo.Tests;

public sealed class DemoResetTests : IAsyncLifetime
{
    private static readonly OwnerId Owner = TestExecutionContext.OwnerA;

    // Bookkeeping that seeding and reset legitimately change: the audit trail of both, and the vector index
    // created with the first embedding. FTS5 and vec0 shadow tables keep their own segments.
    private static readonly string[] _ignored = ["audit_events", "memory_vector_index", "__EFMigrationsHistory"];

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 9, 30, 0, TimeSpan.Zero));
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Reset_RemovesSeededRowsKeepsTheUsersTask()
    {
        Dictionary<string, long> before = await CountsAsync();
        await Seeder().SeedAsync(Owner, Ct);
        TaskId mine = await AddUserTaskAsync();

        int removed = await Reset().ResetAsync(Owner, Ct);

        Dictionary<string, long> after = await CountsAsync();
        Assert.True(removed > 0);
        foreach ((string table, long count) in after)
            Assert.True(count == before.GetValueOrDefault(table) + (table == "agent_tasks" ? 1 : 0), $"{table}: {before.GetValueOrDefault(table)} before seeding, {count} after the reset");
        await using MarshalDbContext db = _database.CreateContext(Owner);
        Assert.Equal(mine, (await db.AgentTasks.SingleAsync(Ct)).Id);
        Assert.Empty(await db.DemoSeeds.ToListAsync(Ct));
    }

    [Fact]
    public async Task Reset_AuditsAsSystem()
    {
        await Seeder().SeedAsync(Owner, Ct);

        await Reset().ResetAsync(Owner, Ct);

        await using MarshalDbContext db = _database.CreateContext(Owner);
        List<AuditEvent> audit = await db.AuditEvents.OrderBy(entry => entry.CreatedAt).ThenBy(entry => entry.Id).ToListAsync(Ct);
        Assert.Equal(["demo.seed", "demo.reset"], audit.Select(entry => entry.Action));
        Assert.All(audit, entry => Assert.Equal(AuditActorKind.System, entry.ActorKind));
    }

    [Fact]
    public async Task Reset_UserContinuedASeededConversation_ItsRowsStay()
    {
        await Seeder().SeedAsync(Owner, Ct);
        Run seeded;
        await using (MarshalDbContext db = _database.CreateContext(Owner))
            seeded = await db.Runs.FirstAsync(run => run.SessionId != null, Ct);
        _database.Accessor.Current = TestExecutionContext.Text(Owner) with { SessionId = seeded.SessionId };
        RunId mine = await new DbRunRecorder(_database.Factory, [], Options.Create(new RecordingOptions()), _time).StartRunAsync(new RunStart(_database.Accessor.Current, "turn"), Ct);

        await Reset().ResetAsync(Owner, Ct);

        await using MarshalDbContext after = _database.CreateContext(Owner);
        Assert.Equal(mine, (await after.Runs.SingleAsync(Ct)).Id);
        Assert.Single(await after.Sessions.ToListAsync(Ct));
    }

    [Fact]
    public async Task Reseed_AfterReset_TimesRelativeToTheNewSeedTime()
    {
        await Seeder().SeedAsync(Owner, Ct);
        await Reset().ResetAsync(Owner, Ct);
        _time.Advance(TimeSpan.FromDays(10));

        await Seeder().SeedAsync(Owner, Ct);

        await using MarshalDbContext db = _database.CreateContext(Owner);
        Assert.All(await db.Runs.ToListAsync(Ct), run => Assert.InRange(run.StartedAt, _time.GetUtcNow().AddDays(-3), _time.GetUtcNow()));
    }

    private async Task<TaskId> AddUserTaskAsync()
    {
        await using MarshalDbContext db = _database.CreateContext(Owner);
        var task = new AgentTask
        {
            Id = TaskId.New(),
            OwnerId = Owner,
            CreatedBy = TaskCreator.User,
            Kind = TaskKind.Agent,
            Goal = "Water the plants",
            TriggerKind = TaskTriggerKind.Now,
            TriggerSpec = "",
            CreatedAt = _time.GetUtcNow(),
            UpdatedAt = _time.GetUtcNow(),
        };
        db.AgentTasks.Add(task);
        await db.SaveChangesAsync(Ct);
        return task.Id;
    }

    private async Task<Dictionary<string, long>> CountsAsync()
    {
        await using MarshalDbContext db = _database.CreateContext(Owner);
        DbConnection connection = db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(Ct);
        var tables = new List<string>();
        await using (DbCommand list = connection.CreateCommand())
        {
            list.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%'";
            await using DbDataReader reader = await list.ExecuteReaderAsync(Ct);
            while (await reader.ReadAsync(Ct))
                tables.Add(reader.GetString(0));
        }

        var counts = new Dictionary<string, long>();
        foreach (string table in tables.Where(table => !_ignored.Contains(table) && !IsShadow(table, tables)))
        {
            await using DbCommand count = connection.CreateCommand();
            count.CommandText = $"SELECT count(*) FROM \"{table}\"";
            counts[table] = (long)(await count.ExecuteScalarAsync(Ct))!;
        }

        return counts;
    }

    private static bool IsShadow(string table, List<string> tables) =>
        tables.Any(parent => parent != table && table.StartsWith(parent + "_", StringComparison.Ordinal) && parent is "chunks_fts" or "chunk_versions_fts" or "notes_fts" or "chunk_vectors" or "note_vectors");

    private DemoSeeder Seeder() => new(
        _database.Factory,
        _database.Accessor,
        Memory(),
        new EfUserProfileProvider(_database.Factory),
        new DbAuditLog(_database.Factory, _database.Accessor, _time),
        Options.Create(new RecordingOptions()),
        _time,
        DemoSeedParts.All with { MaintenanceItem = true });

    private DemoReset Reset() => new(_database.Factory, _database.Accessor, Memory(), new DbAuditLog(_database.Factory, _database.Accessor, _time));

    private SqliteMemoryStore Memory() => new(_database.Factory, new FakeEmbeddingGenerator(), _database.Accessor, _time);
}
