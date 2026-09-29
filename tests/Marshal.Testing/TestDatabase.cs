using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Data;
using Marshal.Data.Identity;
using Marshal.Data.Memory;
using Marshal.Data.Notes;
using Marshal.Data.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Testing;

/// <summary>
/// SQLite database for tests: in memory with one shared connection (<see cref="CreateAsync()"/>), or a temporary
/// file with a connection per context for tests with concurrent workers (<see cref="CreateFileAsync"/>).
/// </summary>
public sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection;
    private readonly string? _file;
    private readonly IExecutionContextAccessor? _contextAccessor;

    private TestDatabase(string? file, IExecutionContextAccessor? contextAccessor)
    {
        _file = file;
        _contextAccessor = contextAccessor;
        _connection = new(file is null ? "Data Source=:memory:" : ConnectionString(file));
    }

    /// <summary>The accessor of in-memory databases; file databases use the accessor they were created with.</summary>
    public FakeExecutionContextAccessor Accessor { get; } = new();

    /// <summary>Factory for services that create a context per operation.</summary>
    public IDbContextFactory<MarshalDbContext> Factory => new ContextFactory(this);

    public static Task<TestDatabase> CreateAsync() => InitializeAsync(new TestDatabase(null, null));

    /// <summary>A temporary file database whose contexts each open their own connection and read the owner from <paramref name="accessor"/>.</summary>
    public static Task<TestDatabase> CreateFileAsync(IExecutionContextAccessor accessor) =>
        InitializeAsync(new TestDatabase(Path.Combine(Path.GetTempPath(), $"marshal-test-{Guid.NewGuid():N}.db"), accessor));

    private static async Task<TestDatabase> InitializeAsync(TestDatabase database)
    {
        await database._connection.OpenAsync();
        await using MarshalDbContext context = new(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite(database._connection).Options, database.Accessor);
        await context.Database.EnsureCreatedAsync();
        foreach (string statement in MemorySchema.Create.Concat(NotesSchema.Create))
            await context.Database.ExecuteSqlRawAsync(statement);
        foreach (OwnerId owner in new[] { TestExecutionContext.OwnerA, TestExecutionContext.OwnerB })
        {
            context.Users.Add(new User
            {
                Id = owner,
                UserName = owner.ToString(),
                DisplayName = owner == TestExecutionContext.OwnerA ? "Alice" : "Bob",
                CreatedAt = DateTimeOffset.UnixEpoch,
            });
        }

        await context.SaveChangesAsync();
        return database;
    }

    public MarshalDbContext CreateContext() => _file is null
        ? new(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite(_connection).Options, Accessor)
        : new(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite(ConnectionString(_file)).Options, _contextAccessor!);

    /// <summary>Creates a context that runs as <paramref name="owner"/>.</summary>
    public MarshalDbContext CreateContext(OwnerId owner)
    {
        Accessor.Current = TestExecutionContext.Text(owner);
        return CreateContext();
    }

    public async ValueTask DisposeAsync()
    {
        await _connection.DisposeAsync();
        if (_file is not null)
        {
            SqliteConnection.ClearAllPools();
            File.Delete(_file);
        }
    }

    private static string ConnectionString(string file) => $"Data Source={file};Pooling=False";

    private sealed class ContextFactory(TestDatabase database) : IDbContextFactory<MarshalDbContext>
    {
        public MarshalDbContext CreateDbContext() => database.CreateContext();
    }
}
