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

    [Fact]
    public async Task CostSince_AutomationRunsCount()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await AddRunAsync(database, OwnerA, SessionSource.Task, _since.AddHours(1), 0.25m);
        await AddRunAsync(database, OwnerA, SessionSource.Automation, _since.AddHours(1), 0.5m);
        database.Accessor.Current = Text(OwnerA);

        decimal cost = await new EfTaskSpending(database.Factory).GetCostSinceAsync(_since, TestContext.Current.CancellationToken);

        Assert.Equal(0.75m, cost);
    }

    [Fact]
    public async Task CostSince_McpServerRunsCountOtherApiRunsDoNot()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await AddRunAsync(database, OwnerA, SessionSource.Api, _since.AddHours(1), 0.25m, "mcp:ask_marshal");
        await AddRunAsync(database, OwnerA, SessionSource.Api, _since.AddHours(1), 0.125m, "mcp:memory_recall");
        await AddRunAsync(database, OwnerA, SessionSource.Api, _since.AddHours(1), 1m, "turn");
        database.Accessor.Current = Text(OwnerA);

        decimal cost = await new EfTaskSpending(database.Factory).GetCostSinceAsync(_since, TestContext.Current.CancellationToken);

        Assert.Equal(0.375m, cost);
    }

    private static async Task AddRunAsync(TestDatabase database, OwnerId owner, SessionSource source, DateTimeOffset started, decimal cost, string trigger = "task")
    {
        await using MarshalDbContext context = database.CreateContext(owner);
        context.Runs.Add(new Run { Id = RunId.New(), OwnerId = owner, Source = source, Trigger = trigger, StartedAt = started, Cost = cost });
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }
}
