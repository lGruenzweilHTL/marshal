using Marshal.Abstractions.Identity;
using Marshal.Data.Identity;
using Marshal.Data.Persistence;
using Marshal.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Tests;

/// <summary>
/// In-memory SQLite database shared by the contexts created from it.
/// </summary>
internal sealed class TestDatabase : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");

    private TestDatabase()
    {
    }

    public FakeExecutionContextAccessor Accessor { get; } = new();

    /// <summary>Factory for services that create a context per operation.</summary>
    public IDbContextFactory<MarshalDbContext> Factory => new ContextFactory(this);

    public static async Task<TestDatabase> CreateAsync()
    {
        var database = new TestDatabase();
        await database._connection.OpenAsync();
        await using MarshalDbContext context = database.CreateContext();
        await context.Database.EnsureCreatedAsync();
        foreach (OwnerId owner in new[] { TestExecutionContext.OwnerA, TestExecutionContext.OwnerB })
        {
            context.Users.Add(new User
            {
                Id = owner,
                UserName = owner.ToString(),
                DisplayName = owner == TestExecutionContext.OwnerA ? "Alice" : "Bob",
                SecurityStamp = "stamp",
                CreatedAt = DateTimeOffset.UnixEpoch,
            });
        }

        await context.SaveChangesAsync();
        return database;
    }

    public MarshalDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<MarshalDbContext>().UseMarshalSqlite(_connection).Options, Accessor);

    /// <summary>Creates a context that runs as <paramref name="owner"/>.</summary>
    public MarshalDbContext CreateContext(OwnerId owner)
    {
        Accessor.Current = TestExecutionContext.Text(owner);
        return CreateContext();
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();

    private sealed class ContextFactory(TestDatabase database) : IDbContextFactory<MarshalDbContext>
    {
        public MarshalDbContext CreateDbContext() => database.CreateContext();
    }
}
