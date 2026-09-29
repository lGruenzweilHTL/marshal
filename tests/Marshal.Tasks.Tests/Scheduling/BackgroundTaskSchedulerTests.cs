using System.Collections.Concurrent;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Core.Execution;
using Marshal.Tasks.Deliveries;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Limits;
using Marshal.Tasks.Running;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Scheduling;

public sealed class BackgroundTaskSchedulerTests : IAsyncLifetime
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly AsyncLocalExecutionContextAccessor _accessor = new();
    private TestDatabase _database = null!;
    private FakeRunner _runner = null!;
    private readonly ListLogger _log = new();
    private readonly TaskOptions _options = new();
    private readonly FakeSpending _spending = new();
    private readonly List<ITaskRunner> _extraRunners = [];
    private BackgroundTaskScheduler? _scheduler;
    private readonly TaskSignals _signals = new();
    private readonly RecordingDispatcher _dispatcher;

    public BackgroundTaskSchedulerTests() => _dispatcher = new RecordingDispatcher(_accessor);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateFileAsync(_accessor);
        _runner = new FakeRunner(_time, _accessor);
    }

    public async ValueTask DisposeAsync()
    {
        _runner.ReleaseAll();
        if (_scheduler is not null)
        {
            await _scheduler.StopAsync(CancellationToken.None);
            _scheduler.Dispose();
        }

        _signals.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task At_RunsWhenItsTimeComes()
    {
        TaskRecord task = await CreateAsync(New(TaskTrigger.At(_time.GetUtcNow().AddMinutes(10))));
        await StartAsync();

        await AdvanceToAsync(_time.GetUtcNow().AddMinutes(9));
        Assert.Empty(_runner.Runs);
        await AdvanceToAsync(_time.GetUtcNow().AddMinutes(1));
        await WaitForAsync(() => StatusAsync(task.Id, AgentTaskStatus.Done));

        (TaskId id, DateTimeOffset at, OwnerId? owner) = Assert.Single(_runner.Runs);
        Assert.Equal((task.Id, task.NextRunAt!.Value, OwnerA), (id, at, owner));
        TaskRecord done = await GetAsync(task.Id);
        Assert.Equal(("Done.", _time.GetUtcNow(), 1), (done.Result?.Speech, done.ResultAt, done.Spent.Runs));
        Assert.Null(done.NextRunAt);
    }

    [Fact]
    public async Task Done_ResultDispatchedAsOwner_WaitNot()
    {
        DateTimeOffset wakeAt = _time.GetUtcNow().AddMinutes(30);
        _runner.Outcome = task => task.Notes is null ? TaskRunOutcome.Wait(wakeAt, "checked once") : TaskRunOutcome.Done(new TaskResult("Done.", "Done."));
        TaskRecord task = await CreateAsync(New(TaskTrigger.Now()));
        await StartAsync();

        await WaitForAsync(() => StatusAsync(task.Id, AgentTaskStatus.Waiting));
        Assert.Empty(_dispatcher.Dispatched);
        await AdvanceToAsync(wakeAt);
        await WaitForAsync(() => StatusAsync(task.Id, AgentTaskStatus.Done));

        await WaitForAsync(() => Task.FromResult(!_dispatcher.Dispatched.IsEmpty));
        Assert.Equal((task.Id, (OwnerId?)OwnerA), Assert.Single(_dispatcher.Dispatched));
    }

    [Fact]
    public async Task Cron_SixInViennaOnBothSidesOfDstChange()
    {
        _time.SetUtcNow(new DateTimeOffset(2026, 10, 23, 12, 0, 0, TimeSpan.Zero));
        TaskRecord task = await CreateAsync(New(TaskTrigger.Cron("0 6 * * *")));
        await StartAsync();

        await AdvanceToAsync(new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero)); // 06:00 CEST
        await WaitForAsync(() => Task.FromResult(_runner.Runs.Count == 1));
        await WaitForAsync(() => StatusAsync(task.Id, AgentTaskStatus.Waiting));
        await AdvanceToAsync(new DateTimeOffset(2026, 10, 25, 4, 0, 0, TimeSpan.Zero)); // 05:00 CET, too early
        Assert.Single(_runner.Runs);
        await AdvanceToAsync(new DateTimeOffset(2026, 10, 25, 5, 0, 0, TimeSpan.Zero)); // 06:00 CET
        await WaitForAsync(() => Task.FromResult(_runner.Runs.Count == 2));
        await WaitForAsync(async () => (await GetAsync(task.Id)).ResultAt == new DateTimeOffset(2026, 10, 25, 5, 0, 0, TimeSpan.Zero));

        Assert.Equal([new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 25, 5, 0, 0, TimeSpan.Zero)], _runner.Runs.Select(run => run.At));
        TaskRecord waiting = await GetAsync(task.Id);
        Assert.Equal((AgentTaskStatus.Waiting, new DateTimeOffset(2026, 10, 26, 5, 0, 0, TimeSpan.Zero)), (waiting.Status, waiting.NextRunAt));
    }

    [Fact]
    public async Task After_DueWhenTheOtherTaskIsDone()
    {
        _runner.Gate();
        TaskRecord first = await CreateAsync(New(TaskTrigger.Now()));
        TaskRecord second = await CreateAsync(New(TaskTrigger.After(first.Id)));
        await StartAsync();

        await WaitForAsync(() => Task.FromResult(_runner.Runs.Count == 1));
        Assert.Equal(AgentTaskStatus.Pending, (await GetAsync(second.Id)).Status);
        _runner.ReleaseAll();

        await WaitForAsync(() => StatusAsync(second.Id, AgentTaskStatus.Done));
        Assert.Equal([first.Id, second.Id], _runner.Runs.Select(run => run.Id));
    }

    [Fact]
    public async Task After_OtherTaskFailed_NeverRuns()
    {
        _runner.Outcome = _ => TaskRunOutcome.Failed("tool_error");
        TaskRecord first = await CreateAsync(New(TaskTrigger.Now()));
        TaskRecord second = await CreateAsync(New(TaskTrigger.After(first.Id)));
        await StartAsync();

        await WaitForAsync(() => StatusAsync(first.Id, AgentTaskStatus.Failed));
        await AdvanceToAsync(_time.GetUtcNow().AddDays(1));

        Assert.Equal("tool_error", (await GetAsync(first.Id)).StatusReason);
        Assert.Equal((AgentTaskStatus.Pending, (DateTimeOffset?)null), ((await GetAsync(second.Id)).Status, (await GetAsync(second.Id)).NextRunAt));
        Assert.Single(_runner.Runs);
    }

    [Fact]
    public async Task Wait_WakesAtTheRequestedTimeWithNotes()
    {
        DateTimeOffset wakeAt = _time.GetUtcNow().AddMinutes(30);
        _runner.Outcome = task => task.Notes is null ? TaskRunOutcome.Wait(wakeAt, "checked once") : TaskRunOutcome.Done(new TaskResult("Done.", "Done."));
        TaskRecord task = await CreateAsync(New(TaskTrigger.Now()));
        await StartAsync();

        await WaitForAsync(() => StatusAsync(task.Id, AgentTaskStatus.Waiting));
        Assert.Equal(("checked once", wakeAt), ((await GetAsync(task.Id)).Notes, (await GetAsync(task.Id)).NextRunAt));
        await AdvanceToAsync(wakeAt);

        await WaitForAsync(() => StatusAsync(task.Id, AgentTaskStatus.Done));
        Assert.Equal([_time.GetUtcNow().AddMinutes(-30), wakeAt], _runner.Runs.Select(run => run.At));
    }

    [Fact]
    public async Task Restart_RunningTaskBecomesPendingWithNoteAndRuns()
    {
        TaskRecord task = await CreateAsync(New(TaskTrigger.At(_time.GetUtcNow().AddDays(1))));
        using (_accessor.Use(Text(OwnerA)))
            await Store().UpdateAsync(task with { Status = AgentTaskStatus.Running, NextRunAt = null }, Ct);
        _runner.Gate();

        await StartAsync();

        await WaitForAsync(() => Task.FromResult(_runner.Runs.Count == 1));
        TaskRecord recovered = _runner.Seen.Single();
        Assert.Contains("restart", recovered.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Wake_WhileRunning_NeverRunsTwice()
    {
        _runner.Gate();
        TaskRecord task = await CreateAsync(New(TaskTrigger.Now()));
        await StartAsync(workers: 3);
        await WaitForAsync(() => Task.FromResult(_runner.Runs.Count == 1));

        for (int i = 0; i < 5; i++)
        {
            _signals.Wake();
            await WaitIdleAsync();
        }

        _runner.ReleaseAll();
        await WaitForAsync(() => StatusAsync(task.Id, AgentTaskStatus.Done));
        Assert.Single(_runner.Runs);
    }

    [Fact]
    public async Task PoolFull_NextDueTaskStartsWhenAWorkerIsFree()
    {
        _runner.Gate();
        TaskRecord later = await CreateAsync(New(TaskTrigger.At(_time.GetUtcNow().AddMinutes(-1))));
        TaskRecord earlier = await CreateAsync(New(TaskTrigger.At(_time.GetUtcNow().AddMinutes(-2))));
        await StartAsync(workers: 1);

        await WaitForAsync(() => Task.FromResult(_runner.Runs.Count == 1));
        await WaitIdleAsync();
        Assert.Equal([earlier.Id], _runner.Runs.Select(run => run.Id));
        _runner.ReleaseAll();

        await WaitForAsync(() => StatusAsync(later.Id, AgentTaskStatus.Done));
        Assert.Equal([earlier.Id, later.Id], _runner.Runs.Select(run => run.Id));
    }

    [Fact]
    public async Task CancelledDuringRun_StaysCancelled()
    {
        TaskRecord task = await CreateAsync(New(TaskTrigger.Now()));
        _runner.During = async running =>
        {
            TaskRecord current = (await Store().GetAsync(running.Id, Ct)).Value;
            await Store().UpdateAsync(current with { Status = AgentTaskStatus.Cancelled, NextRunAt = null }, Ct);
        };
        await StartAsync();

        await WaitForAsync(() => Task.FromResult(_runner.Finished == 1));
        await WaitIdleAsync();

        TaskRecord cancelled = await GetAsync(task.Id);
        Assert.Equal((AgentTaskStatus.Cancelled, (TaskResult?)null), (cancelled.Status, cancelled.Result));
    }

    [Fact]
    public async Task BudgetUsedUp_FailsWithoutRunning()
    {
        TaskRecord task = await CreateAsync(New(TaskTrigger.Now()) with { Budget = new TaskBudget(Runs: 0) });
        await StartAsync();

        await WaitForAsync(() => StatusAsync(task.Id, AgentTaskStatus.Failed));

        Assert.Equal("budget_exceeded", (await GetAsync(task.Id)).StatusReason);
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public async Task DailyCapReached_PostponedToTheNextLocalDay()
    {
        _spending.Cost = _options.DailyCostCap;
        TaskRecord task = await CreateAsync(New(TaskTrigger.Now()));
        await StartAsync();

        await WaitForAsync(async () => (await GetAsync(task.Id)).StatusReason == "daily_cap");

        TaskRecord postponed = await GetAsync(task.Id);
        Assert.Equal((AgentTaskStatus.Pending, new DateTimeOffset(2026, 9, 29, 22, 0, 0, TimeSpan.Zero)), (postponed.Status, postponed.NextRunAt));
        Assert.Empty(_runner.Runs);
    }

    [Fact]
    public async Task RunLimits_PassedToTheRunnerAndSpendDrawnFromParent()
    {
        TaskRecord parent = await CreateAsync(New(TaskTrigger.At(_time.GetUtcNow().AddDays(1))) with { Budget = new TaskBudget(Cost: 0.3m) });
        TaskRecord child = await CreateAsync(New(TaskTrigger.Now()) with { ParentId = parent.Id, Budget = new TaskBudget(Cost: 1m) });
        _runner.Outcome = _ => TaskRunOutcome.Done(new TaskResult("Done.", "Done.")) with { Spend = new TaskSpend(Tokens: 500, Cost: 0.1m, Steps: 3) };
        await StartAsync();

        await WaitForAsync(() => StatusAsync(child.Id, AgentTaskStatus.Done));

        Assert.Equal(0.3m, _runner.Limits.Single().MaxCost);
        Assert.Equal(new TaskSpend(500, 0.1m, 3, TimeSpan.Zero, 1), (await GetAsync(child.Id)).Spent);
        Assert.Equal(new TaskSpend(500, 0.1m, 3, TimeSpan.Zero, 1), (await GetAsync(parent.Id)).Spent);
    }

    [Fact]
    public async Task Cron_DoneRunStartsTheNextActivationWithAFreshBudget()
    {
        TaskRecord task = await CreateAsync(New(TaskTrigger.Cron("0 6 * * *")));
        _runner.Outcome = _ => TaskRunOutcome.Done(new TaskResult("Done.", "Done.")) with { Spend = new TaskSpend(Cost: 0.1m) };
        await StartAsync();

        await AdvanceToAsync(task.NextRunAt!.Value);
        await WaitForAsync(() => Task.FromResult(_runner.Finished == 1));
        await WaitForAsync(async () => (await GetAsync(task.Id)).ResultAt is not null);

        Assert.Equal(TaskSpend.Zero, (await GetAsync(task.Id)).Spent);
    }

    [Fact]
    public async Task CancelRun_StopsTheRunAndKeepsTheCancelledStatus()
    {
        _runner.Gate();
        TaskRecord task = await CreateAsync(New(TaskTrigger.Now()));
        await StartAsync();
        await WaitForAsync(() => Task.FromResult(_runner.Runs.Count == 1));

        using (_accessor.Use(Text(OwnerA)))
            await Store().UpdateAsync((await Store().GetAsync(task.Id, Ct)).Value with { Status = AgentTaskStatus.Cancelled }, Ct);
        _signals.CancelRun(task.Id);

        await WaitForAsync(() => Task.FromResult(_runner.Finished == 1));
        await WaitIdleAsync();
        Assert.Equal(AgentTaskStatus.Cancelled, (await GetAsync(task.Id)).Status);
    }

    [Fact]
    public async Task KillSwitchActive_AgentTaskWaitsSystemTaskRuns()
    {
        var system = new FakeRunner(_time, _accessor, TaskKind.System);
        _extraRunners.Add(system);
        TaskRecord agent = await CreateAsync(New(TaskTrigger.Now()));
        TaskRecord backup = await CreateAsync(New(TaskTrigger.Now()) with { Kind = TaskKind.System, Job = "backup" });
        await PauseAsync();
        await StartAsync();

        await WaitForAsync(() => StatusAsync(backup.Id, AgentTaskStatus.Done));
        await WaitForAsync(async () => (await GetAsync(agent.Id)).StatusReason == "paused");

        Assert.Empty(_runner.Runs);
        Assert.Null((await GetAsync(agent.Id)).NextRunAt);
    }

    [Fact]
    public async Task RunnerThrows_TaskFailed()
    {
        _runner.Outcome = _ => throw new InvalidOperationException("boom");
        TaskRecord task = await CreateAsync(New(TaskTrigger.Now()));
        await StartAsync();

        await WaitForAsync(() => StatusAsync(task.Id, AgentTaskStatus.Failed));

        Assert.Equal("error", (await GetAsync(task.Id)).StatusReason);
    }

    private EfTaskStore Store() => new(_database.Factory, _accessor, new ViennaProfiles(), Options.Create(_options), _time);

    private async Task StartAsync(int workers = 2)
    {
        _options.Workers = workers;
        var budgets = new TaskBudgets(Store(), _spending, new ViennaProfiles(), new FakeTemplateCatalog(), Options.Create(_options), _time);
        _scheduler = new BackgroundTaskScheduler(Store(), _signals, [_runner, .. _extraRunners], _dispatcher, budgets, new TaskPauses(_database.Factory), _accessor, new ViennaProfiles(), Options.Create(_options), _time, _log);
        await _scheduler.StartAsync(Ct);
        await WaitIdleAsync();
    }

    private async Task<TaskRecord> CreateAsync(NewTask task)
    {
        using (_accessor.Use(Text(OwnerA)))
            return (await Store().CreateAsync(task, Ct)).Value;
    }

    private async Task<TaskRecord> GetAsync(TaskId id)
    {
        using (_accessor.Use(Text(OwnerA)))
            return (await Store().GetAsync(id, Ct)).Value;
    }

    private async Task<bool> StatusAsync(TaskId id, AgentTaskStatus status) => (await GetAsync(id)).Status == status;

    // Moves the clock only while the scheduler waits, so its timer is already registered.
    private async Task AdvanceToAsync(DateTimeOffset time)
    {
        await WaitIdleAsync();
        _time.SetUtcNow(time);
        await WaitIdleAsync();
    }

    private async Task WaitIdleAsync()
    {
        await WaitForAsync(() => Task.FromResult(_scheduler!.IsIdle));
        // Idle must hold on two consecutive looks: a timer that just fired may still be on its way to the loop.
        await Task.Delay(20, Ct);
        await WaitForAsync(() => Task.FromResult(_scheduler!.IsIdle));
    }

    private async Task WaitForAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow + _patience;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Condition not reached in time. " + string.Join(" | ", _log.Entries));
            await Task.Delay(10, Ct);
        }
    }

    private static NewTask New(TaskTrigger trigger) => new() { Goal = "Do the thing.", Trigger = trigger };

    private async Task PauseAsync()
    {
        await using Marshal.Data.MarshalDbContext db = _database.CreateContext();
        db.UserPreferences.Add(new Marshal.Data.Identity.UserPreferences { UserId = OwnerA, Language = "de", TimeZone = "Europe/Vienna", TasksPausedAt = _time.GetUtcNow() });
        await db.SaveChangesAsync(Ct);
    }

    private sealed class RecordingDispatcher(IExecutionContextAccessor accessor) : IDeliveryDispatcher
    {
        public ConcurrentQueue<(TaskId Id, OwnerId? Owner)> Dispatched { get; } = new();

        public Task DispatchAsync(TaskId task, CancellationToken cancellationToken)
        {
            Dispatched.Enqueue((task, accessor.Current?.OwnerId));
            return Task.CompletedTask;
        }

        public Task ReviewPendingAsync(string? changedEntity, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeRunner(TimeProvider time, IExecutionContextAccessor accessor, TaskKind kind = TaskKind.Agent) : ITaskRunner
    {
        private TaskCompletionSource? _gate;
        private int _finished;

        public ConcurrentQueue<(TaskId Id, DateTimeOffset At, OwnerId? Owner)> Runs { get; } = new();

        public ConcurrentQueue<TaskRecord> Seen { get; } = new();

        public Func<TaskRecord, TaskRunOutcome> Outcome { get; set; } = _ => TaskRunOutcome.Done(new TaskResult("Done.", "**Done.**")) with { Spend = new TaskSpend(Runs: 0) };

        public Func<TaskRecord, Task>? During { get; set; }

        public ConcurrentQueue<RunLimits> Limits { get; } = new();

        public int Finished => Volatile.Read(ref _finished);

        public TaskKind Kind => kind;

        public void Gate() => _gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        public void ReleaseAll() => _gate?.TrySetResult();

        public async Task<TaskRunOutcome> RunAsync(TaskRecord task, RunLimits limits, CancellationToken cancellationToken)
        {
            Limits.Enqueue(limits);
            Runs.Enqueue((task.Id, time.GetUtcNow(), accessor.Current?.OwnerId));
            Seen.Enqueue(task);
            try
            {
                if (_gate is not null)
                    await _gate.Task.WaitAsync(cancellationToken);
                if (During is not null)
                    await During(task);
                return Outcome(task);
            }
            finally
            {
                Interlocked.Increment(ref _finished);
            }
        }
    }

    private sealed class FakeSpending : ITaskSpending
    {
        public decimal Cost { get; set; }

        public Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(Cost);
    }

    private sealed class ListLogger : ILogger<BackgroundTaskScheduler>
    {
        public ConcurrentQueue<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Enqueue($"{logLevel}: {formatter(state, exception)} {exception?.GetBaseException().Message}");
    }
}
