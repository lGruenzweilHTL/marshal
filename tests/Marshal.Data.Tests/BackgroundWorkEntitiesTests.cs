using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Data.Notifications;
using Marshal.Data.Tasks;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class BackgroundWorkEntitiesTests : IAsyncLifetime
{
    private static readonly DateTimeOffset _now = new(2026, 9, 29, 10, 0, 0, TimeSpan.Zero);
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task AgentTask_RoundTripsAndIsOwnerScoped()
    {
        AgentTask task = NewTask(OwnerA, "briefing");
        await AddAsync(OwnerA, task);
        await AddAsync(OwnerB, NewTask(OwnerB, "briefing"));

        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        AgentTask stored = Assert.Single(await context.AgentTasks.ToListAsync(Ct));

        Assert.Equal((task.Id, "briefing", TaskTriggerKind.Cron, "0 6 * * *", AgentTaskStatus.Pending, _now), (stored.Id, stored.Name, stored.TriggerKind, stored.TriggerSpec, stored.Status, stored.NextRunAt));
    }

    [Fact]
    public async Task AgentTask_DuplicateNamePerOwner_Rejected()
    {
        await AddAsync(OwnerA, NewTask(OwnerA, "briefing"));

        await Assert.ThrowsAsync<DbUpdateException>(() => AddAsync(OwnerA, NewTask(OwnerA, "briefing")));
    }

    [Fact]
    public async Task AgentTask_UnnamedTasksDoNotCollide()
    {
        await AddAsync(OwnerA, NewTask(OwnerA, null));
        await AddAsync(OwnerA, NewTask(OwnerA, null));

        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        Assert.Equal(2, await context.AgentTasks.CountAsync(Ct));
    }

    [Fact]
    public async Task AgentTask_ConcurrentStatusChange_SecondWriterFails()
    {
        AgentTask task = NewTask(OwnerA, null);
        await AddAsync(OwnerA, task);

        await using MarshalDbContext first = _database.CreateContext(OwnerA);
        await using MarshalDbContext second = _database.CreateContext(OwnerA);
        AgentTask a = await first.AgentTasks.SingleAsync(Ct);
        AgentTask b = await second.AgentTasks.SingleAsync(Ct);
        a.Status = AgentTaskStatus.Running;
        b.Status = AgentTaskStatus.Running;
        await first.SaveChangesAsync(Ct);

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync(Ct));
        Assert.Equal(1, a.RowVersion);
    }

    [Fact]
    public async Task NotificationsProposalsFollowUpsExecutorSessions_AreOwnerScoped()
    {
        foreach (OwnerId owner in new[] { OwnerA, OwnerB })
        {
            AgentTask task = NewTask(owner, null);
            await AddAsync(owner, task);
            await AddAsync(owner, new Notification
            {
                Id = Guid.CreateVersion7(),
                OwnerId = owner,
                TaskId = task.Id,
                Source = $"task:{task.Id}",
                Priority = NotificationPriority.Normal,
                Content = "Print done.",
                CreatedAt = _now,
            });
            await AddAsync(owner, new Proposal
            {
                Id = Guid.CreateVersion7(),
                OwnerId = owner,
                TaskId = task.Id,
                Kind = ProposalKind.Code,
                Target = "marshal",
                Branch = "marshal/fix",
                Title = "Fix",
                Diff = "--- a\n+++ b",
                CreatedAt = _now,
            });
            await AddAsync(owner, new FollowUp
            {
                Id = Guid.CreateVersion7(),
                OwnerId = owner,
                Nonce = $"nonce-{owner}",
                SatelliteId = "kitchen",
                TaskId = task.Id,
                CreatedAt = _now,
                ExpiresAt = _now.AddMinutes(1),
            });
            await AddAsync(owner, new ExecutorSession
            {
                Id = Guid.CreateVersion7(),
                OwnerId = owner,
                TaskId = task.Id,
                Executor = "claude-code",
                Handle = "session-1",
                StartedAt = _now,
            });
        }

        await using MarshalDbContext context = _database.CreateContext(OwnerB);

        Assert.Equal(OwnerB, Assert.Single(await context.Notifications.ToListAsync(Ct)).OwnerId);
        Assert.Equal(NotificationOutcome.Pending, (await context.Notifications.SingleAsync(Ct)).Outcome);
        Assert.Equal(ProposalStatus.Open, Assert.Single(await context.Proposals.ToListAsync(Ct)).Status);
        Assert.Equal($"nonce-{OwnerB}", Assert.Single(await context.FollowUps.ToListAsync(Ct)).Nonce);
        Assert.Equal(ExecutorSessionStatus.Starting, Assert.Single(await context.ExecutorSessions.ToListAsync(Ct)).Status);
    }

    private async Task AddAsync<TEntity>(OwnerId owner, TEntity entity)
        where TEntity : class
    {
        await using MarshalDbContext context = _database.CreateContext(owner);
        context.Add(entity);
        await context.SaveChangesAsync(Ct);
    }

    private static AgentTask NewTask(OwnerId owner, string? name) => new()
    {
        Id = TaskId.New(),
        OwnerId = owner,
        CreatedBy = TaskCreator.User,
        Kind = TaskKind.Agent,
        Goal = "Prepare the morning briefing.",
        TriggerKind = TaskTriggerKind.Cron,
        TriggerSpec = "0 6 * * *",
        NextRunAt = _now,
        Name = name,
        CreatedAt = _now,
        UpdatedAt = _now,
    };
}
