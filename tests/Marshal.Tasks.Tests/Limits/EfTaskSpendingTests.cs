using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Data;
using Marshal.Data.Conversations;
using Marshal.Tasks.Limits;
using Marshal.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Limits;

public sealed class EfTaskSpendingTests
{
    private static readonly DateTimeOffset _since = new(2026, 9, 28, 22, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CostSince_OnlyTheOwnersTaskRunsSinceTheTime()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await AddRunAsync(database, OwnerA, SessionSource.Task, _since.AddHours(1), 0.25m);
        await AddRunAsync(database, OwnerA, SessionSource.Task, _since.AddHours(2), 0.5m);
        await AddRunAsync(database, OwnerA, SessionSource.Web, _since.AddHours(1), 1m);
        await AddRunAsync(database, OwnerA, SessionSource.Task, _since.AddHours(-1), 1m);
        await AddRunAsync(database, OwnerB, SessionSource.Task, _since.AddHours(1), 1m);
        database.Accessor.Current = Text(OwnerA);

        decimal cost = await new EfTaskSpending(database.Factory).GetCostSinceAsync(_since, TestContext.Current.CancellationToken);

        Assert.Equal(0.75m, cost);
    }

    private static async Task AddRunAsync(TestDatabase database, OwnerId owner, SessionSource source, DateTimeOffset started, decimal cost)
    {
        await using MarshalDbContext context = database.CreateContext(owner);
        context.Runs.Add(new Run { Id = RunId.New(), OwnerId = owner, Source = source, Trigger = "task", StartedAt = started, Cost = cost });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
