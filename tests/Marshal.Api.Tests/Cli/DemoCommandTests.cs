using Marshal.Abstractions.Identity;
using Marshal.Api.Cli;
using Marshal.Core.Execution;
using Marshal.Data;
using Marshal.Data.Audit;
using Marshal.Data.Conversations;
using Marshal.Data.Identity;
using Marshal.Data.Persistence;
using Marshal.Demo.Seeding;
using Marshal.Memory;
using Marshal.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Api.Tests.Cli;

public sealed class DemoCommandTests : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 13, 9, 30, 0, TimeSpan.Zero));
    private readonly AsyncLocalExecutionContextAccessor _accessor = new();
    private readonly StringWriter _output = new();
    private readonly StringWriter _error = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private IDbContextFactory<MarshalDbContext> Factory => new ContextFactory(_connection, _accessor);

    [Fact]
    public async Task Seed_ThenReset_SeedsAndRemovesTheDemoData()
    {
        await InitializeAsync();

        int seeded = await Command().RunAsync(["seed"], Ct);
        int runsAfterSeed = await RunCountAsync();
        int reset = await Command().RunAsync(["reset"], Ct);

        Assert.Equal((0, 0), (seeded, reset));
        Assert.True(runsAfterSeed > 0);
        Assert.Equal(0, await RunCountAsync());
        Assert.Contains("Seeded", _output.ToString(), StringComparison.Ordinal);
        Assert.Contains("Removed", _output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seed_Twice_FailsTheSecondTime()
    {
        await InitializeAsync();
        await Command().RunAsync(["seed"], Ct);

        int again = await Command().RunAsync(["seed"], Ct);

        Assert.Equal(1, again);
        Assert.Contains("marshal demo reset", _error.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Seed_DemoOff_Refused()
    {
        await InitializeAsync();

        int exit = await Command(demoEnabled: false).RunAsync(["seed"], Ct);

        Assert.Equal(1, exit);
        Assert.Equal(0, await RunCountAsync());
    }

    [Fact]
    public async Task UnknownSubcommand_PrintsUsage()
    {
        await InitializeAsync();

        int exit = await Command().RunAsync(["wipe"], Ct);

        Assert.Equal(2, exit);
        Assert.Contains("marshal demo reset", _error.ToString(), StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private DemoCommand Command(bool demoEnabled = true)
    {
        var memory = new SqliteMemoryStore(Factory, new FakeEmbeddingGenerator(), _accessor, _time);
        var audit = new DbAuditLog(Factory, _accessor, _time);
        var seeder = new DemoSeeder(Factory, _accessor, memory, new EfUserProfileProvider(Factory), audit, Options.Create(new RecordingOptions()), _time, DemoSeedParts.All);
        return new DemoCommand(seeder, new DemoReset(Factory, _accessor, memory, audit), new EfInstanceOwner(Factory), demoEnabled, _output, _error);
    }

    private async Task InitializeAsync()
    {
        await _connection.OpenAsync(Ct);
        await using var context = new MarshalDbContext(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite(_connection).Options, _accessor);
        await new DatabaseInitializer(context, _time, NullLogger<DatabaseInitializer>.Instance).InitializeAsync(new InstanceOwnerSeed("Lukas", "en", "Europe/Vienna"), Ct);
    }

    private async Task<int> RunCountAsync()
    {
        OwnerId owner = await new EfInstanceOwner(Factory).GetAsync(Ct);
        using IDisposable scope = _accessor.Use(new Abstractions.Execution.MarshalExecutionContext { OwnerId = owner, Source = Abstractions.Execution.SessionSource.Api });
        await using MarshalDbContext db = await Factory.CreateDbContextAsync(Ct);
        return await db.Runs.CountAsync(Ct);
    }

    private sealed class ContextFactory(SqliteConnection connection, AsyncLocalExecutionContextAccessor accessor) : IDbContextFactory<MarshalDbContext>
    {
        public MarshalDbContext CreateDbContext() =>
            new(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite(connection).Options, accessor);
    }
}
