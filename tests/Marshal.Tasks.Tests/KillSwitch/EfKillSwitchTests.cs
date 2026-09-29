using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Tasks;
using Marshal.Data.Identity;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.KillSwitch;

public sealed class EfKillSwitchTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeAuditLog _audit = new();
    private readonly RecordingScheduler _scheduler = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Activate_CancelsUnfinishedAgentTasksStopsRunsAndAudits()
    {
        TaskRecord pending = await CreateAsync(TaskKind.Agent);
        TaskRecord running = await SetStatusAsync(await CreateAsync(TaskKind.Agent), AgentTaskStatus.Running);
        TaskRecord done = await SetStatusAsync(await CreateAsync(TaskKind.Agent), AgentTaskStatus.Done);
        TaskRecord backup = await CreateAsync(TaskKind.System);
        _database.Accessor.Current = Text(OwnerB);
        TaskRecord foreign = await CreateAsync(TaskKind.Agent);
        _database.Accessor.Current = Text(OwnerA) with { RunId = Marshal.Abstractions.Runs.RunId.New() };

        int cancelled = await Switch().ActivateAsync(Ct);

        Assert.Equal(2, cancelled);
        Assert.Equal(AgentTaskStatus.Cancelled, await StatusAsync(pending.Id));
        Assert.Equal(AgentTaskStatus.Cancelled, await StatusAsync(running.Id));
        Assert.Equal(AgentTaskStatus.Done, await StatusAsync(done.Id));
        Assert.Equal(AgentTaskStatus.Pending, await StatusAsync(backup.Id));
        Assert.Equal(new HashSet<TaskId> { pending.Id, running.Id }, _scheduler.Cancelled.ToHashSet());
        Assert.True(await Switch().IsActiveAsync(Ct));
        AuditEntry entry = Assert.Single(_audit.Entries);
        Assert.Equal(("tasks.stop_all", AuditActorKind.User, _database.Accessor.Current!.RunId), (entry.Action, entry.ActorKind, entry.RunId));

        _database.Accessor.Current = Text(OwnerB);
        Assert.False(await Switch().IsActiveAsync(Ct));
        Assert.Equal(AgentTaskStatus.Pending, await StatusAsync(foreign.Id));
    }

    [Fact]
    public async Task Release_EndsThePauseAndAudits()
    {
        await Switch().ActivateAsync(Ct);

        await Switch().ReleaseAsync(Ct);

        Assert.False(await Switch().IsActiveAsync(Ct));
        Assert.Equal(["tasks.stop_all", "tasks.resume"], _audit.Entries.Select(entry => entry.Action));
        Assert.True(_scheduler.Woken > 0);
    }

    private EfKillSwitch Switch() => new(_database.Factory, Store(), new EfUserPreferencesStore(_database.Factory, new FixedInstanceTimeZone()), _scheduler, _audit, _database.Accessor, _time);

    private EfTaskStore Store() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private async Task<TaskRecord> CreateAsync(TaskKind kind) =>
        (await Store().CreateAsync(new NewTask { Goal = "Do it.", Trigger = TaskTrigger.At(_time.GetUtcNow().AddHours(1)), Kind = kind, Job = kind == TaskKind.System ? "backup" : null }, Ct)).Value;

    private async Task<TaskRecord> SetStatusAsync(TaskRecord task, AgentTaskStatus status) =>
        (await Store().UpdateAsync(task with { Status = status }, Ct)).Value;

    private async Task<AgentTaskStatus> StatusAsync(TaskId id)
    {
        await using Marshal.Data.MarshalDbContext db = _database.CreateContext();
        return Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.IgnoreQueryFilters(db.AgentTasks).Single(task => task.Id == id).Status;
    }

    private sealed class RecordingScheduler : ITaskScheduler
    {
        public List<TaskId> Cancelled { get; } = [];

        public int Woken { get; private set; }

        public void Wake() => Woken++;

        public void CancelRun(TaskId task) => Cancelled.Add(task);
    }
}
