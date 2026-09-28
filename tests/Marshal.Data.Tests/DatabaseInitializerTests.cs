using Marshal.Abstractions.Identity;
using Marshal.Data.Identity;
using Marshal.Data.Persistence;
using Marshal.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Data.Tests;

public sealed class DatabaseInitializerTests : IDisposable
{
    private static readonly InstanceOwnerSeed _seed = new("Lukas", "de", "Europe/Vienna");

    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-db-").FullName;
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 7, 30, 15, TimeSpan.Zero));

    private string DatabasePath => Path.Combine(_directory, "marshal.db");

    [Fact]
    public async Task Initialize_PendingMigrationOnExistingFile_CopiesDatabaseBeforeMigrating()
    {
        await CreateLegacyDatabaseAsync();

        await InitializeAsync();

        string backup = Path.Combine(_directory, "marshal.db.20260928073015.bak");
        Assert.True(File.Exists(backup));
        Assert.Equal(["legacy_marker"], await TableNamesAsync(backup));
        Assert.Contains("sessions", await TableNamesAsync(DatabasePath));
    }

    [Fact]
    public async Task Initialize_NoPendingMigration_CreatesNoCopy()
    {
        await InitializeAsync();
        _time.Advance(TimeSpan.FromMinutes(1));

        await InitializeAsync();

        Assert.Empty(Directory.GetFiles(_directory, "*.bak"));
    }

    [Fact]
    public async Task Initialize_EmptyDatabase_SeedsInstanceOwnerOnce()
    {
        await InitializeAsync();
        await InitializeAsync();

        await using MarshalDbContext context = CreateContext();
        User owner = Assert.Single(await context.Users.ToListAsync(TestContext.Current.CancellationToken));
        UserPreferences preferences = Assert.Single(await context.UserPreferences.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Lukas", owner.DisplayName);
        Assert.Equal(owner.Id, preferences.UserId);
        Assert.Equal("de", preferences.Language);
        Assert.Equal("Europe/Vienna", preferences.TimeZone);
    }

    [Fact]
    public async Task Initialize_CoreAgentDatabase_KeepsInstanceOwnerIdAsIdentityUser()
    {
        var ownerId = Guid.CreateVersion7();
        await using (MarshalDbContext context = CreateContext())
            await context.GetService<IMigrator>().MigrateAsync("OpenSessionUniqueness", cancellationToken: TestContext.Current.CancellationToken);
        await ExecuteAsync($"INSERT INTO users (id, user_name, display_name, security_stamp, created_at) VALUES ('{ownerId.ToString().ToUpperInvariant()}', 'owner', 'Lukas', 'stamp', 0)");

        await InitializeAsync();

        await using MarshalDbContext migrated = CreateContext();
        User owner = Assert.Single(await migrated.Users.ToListAsync(TestContext.Current.CancellationToken));
        Assert.Equal(new OwnerId(ownerId), owner.Id);
        Assert.Equal("Lukas", owner.DisplayName);
        Assert.Equal("OWNER", owner.NormalizedUserName);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        Directory.Delete(_directory, recursive: true);
    }

    private MarshalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite($"Data Source={DatabasePath}").Options, new FakeExecutionContextAccessor());

    private async Task InitializeAsync()
    {
        await using MarshalDbContext context = CreateContext();
        var initializer = new DatabaseInitializer(context, _time, NullLogger<DatabaseInitializer>.Instance);
        await initializer.InitializeAsync(_seed, TestContext.Current.CancellationToken);
    }

    private async Task CreateLegacyDatabaseAsync()
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "CREATE TABLE legacy_marker (id INTEGER)";
        await command.ExecuteNonQueryAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqliteConnection($"Data Source={DatabasePath}");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<List<string>> TableNamesAsync(string path)
    {
        await using var connection = new SqliteConnection($"Data Source={path};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'sqlite_%' AND name NOT LIKE '__EF%' ORDER BY name";
        var names = new List<string>();
        await using SqliteDataReader reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            names.Add(reader.GetString(0));

        return names;
    }
}
