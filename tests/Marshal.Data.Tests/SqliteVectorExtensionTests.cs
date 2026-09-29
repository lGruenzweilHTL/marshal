using Marshal.Abstractions;
using Marshal.Data.Persistence;
using Marshal.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Tests;

public sealed class SqliteVectorExtensionTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-vec-").FullName;

    [Fact]
    public async Task EveryConnection_LoadsTheExtension()
    {
        DbContextOptions<MarshalDbContext> options = new DbContextOptionsBuilder<MarshalDbContext>()
            .UseMarshalSqlite($"Data Source={Path.Combine(_directory, "marshal.db")}")
            .Options;

        for (int attempt = 0; attempt < 2; attempt++)
        {
            await using var db = new MarshalDbContext(options, new FakeExecutionContextAccessor());

            Result<string> version = await SqliteVectorExtension.ProbeAsync(db, Ct);

            Assert.True(version.IsSuccess, version.IsFailure ? version.Error.Message : null);
            Assert.StartsWith("v", version.Value, StringComparison.Ordinal);
            SqliteConnection.ClearAllPools();
        }
    }

    [Fact]
    public async Task TestDatabase_HasTheExtension()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext db = database.CreateContext();

        Assert.True((await SqliteVectorExtension.ProbeAsync(db, Ct)).IsSuccess);
    }

    [Fact]
    public async Task ConnectionWithoutTheExtension_ProbeFailsWithReason()
    {
        DbContextOptions<MarshalDbContext> options = new DbContextOptionsBuilder<MarshalDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_directory, "plain.db")}")
            .Options;
        await using var db = new MarshalDbContext(options, new FakeExecutionContextAccessor());

        Result<string> version = await SqliteVectorExtension.ProbeAsync(db, Ct);

        Assert.True(version.IsFailure);
        Assert.Equal("memory.vector_extension_missing", version.Error.Code);
        Assert.Contains("vec_version", version.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Migrate_CreatesTheMemoryIndexTablesOfMemorySchema()
    {
        DbContextOptions<MarshalDbContext> options = new DbContextOptionsBuilder<MarshalDbContext>()
            .UseMarshalSqlite($"Data Source={Path.Combine(_directory, "migrated.db")}")
            .Options;
        await using var db = new MarshalDbContext(options, new FakeExecutionContextAccessor());

        await db.Database.MigrateAsync(Ct);

        List<string> tables = await db.Database.SqlQueryRaw<string>("SELECT name AS \"Value\" FROM sqlite_master WHERE name IN ('chunks_fts', 'memory_vector_index')").ToListAsync(Ct);
        Assert.Equal(["chunks_fts", "memory_vector_index"], tables.Order(StringComparer.Ordinal));
        foreach (string statement in Marshal.Data.Memory.MemorySchema.Create)
            await db.Database.ExecuteSqlRawAsync(statement, Ct);
    }

    [Fact]
    public async Task Migrate_CreatesTheNotesIndexTablesOfNotesSchema()
    {
        DbContextOptions<MarshalDbContext> options = new DbContextOptionsBuilder<MarshalDbContext>()
            .UseMarshalSqlite($"Data Source={Path.Combine(_directory, "notes.db")}")
            .Options;
        await using var db = new MarshalDbContext(options, new FakeExecutionContextAccessor());

        await db.Database.MigrateAsync(Ct);

        List<string> tables = await db.Database.SqlQueryRaw<string>("SELECT name AS \"Value\" FROM sqlite_master WHERE name IN ('notes_fts', 'notes_vector_index', 'note_index_entries')").ToListAsync(Ct);
        Assert.Equal(["note_index_entries", "notes_fts", "notes_vector_index"], tables.Order(StringComparer.Ordinal));
        foreach (string statement in Marshal.Data.Notes.NotesSchema.Create)
            await db.Database.ExecuteSqlRawAsync(statement, Ct);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
