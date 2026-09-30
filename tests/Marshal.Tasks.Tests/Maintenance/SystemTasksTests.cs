using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Tasks.Maintenance;
using Marshal.Tasks.Running;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Maintenance;

public sealed class SystemTasksTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly StubJob _backup = new(BackupJob.JobName);
    private readonly StubJob _retention = new(RetentionPurgeJob.JobName);
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Seed_CronSystemTasksForTheInstanceOwner_OnlyForRegisteredJobs()
    {
        _database.Accessor.Current = null;

        await Seeder().StartAsync(Ct);

        _database.Accessor.Current = Text(OwnerA);
        IReadOnlyList<TaskRecord> seeded = await Tasks().ListAsync(new TaskFilter(), Ct);
        Assert.Equal(["system-backup", "system-retention"], seeded.Select(task => task.Name).Order(StringComparer.Ordinal));
        TaskRecord backup = seeded.Single(task => task.Name == "system-backup");
        Assert.Equal((TaskKind.System, "backup", TaskCreator.System, TaskTrigger.Cron("0 3 * * *")), (backup.Kind, backup.Job, backup.CreatedBy, backup.Trigger));
        // 03:00 in Vienna is 01:00 UTC.
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 1, 0, 0, TimeSpan.Zero), backup.NextRunAt);
    }

    [Fact]
    public async Task Seed_Twice_IdempotentByName_NewScheduleAndCancelledTaskArmedAgain()
    {
        await Seeder().StartAsync(Ct);
        TaskRecord backup = (await Tasks().FindAsync("system-backup", Ct)).Value;
        await Tasks().UpdateAsync(backup with { Status = AgentTaskStatus.Cancelled, NextRunAt = null }, Ct);

        await Seeder(new MaintenanceOptions { BackupSchedule = "15 4 * * *" }).StartAsync(Ct);

        IReadOnlyList<TaskRecord> seeded = await Tasks().ListAsync(new TaskFilter(), Ct);
        Assert.Equal(2, seeded.Count);
        TaskRecord armed = seeded.Single(task => task.Name == "system-backup");
        Assert.Equal((AgentTaskStatus.Pending, TaskTrigger.Cron("15 4 * * *"), (DateTimeOffset?)new DateTimeOffset(2026, 9, 30, 2, 15, 0, TimeSpan.Zero)),
            (armed.Status, armed.Trigger, armed.NextRunAt));
    }

    [Fact]
    public async Task Runner_RunsTheNamedJob_ResultIsItsSummary()
    {
        TaskRunOutcome outcome = await Runner().RunAsync(SystemTask("backup"), new RunLimits(), Ct);

        Assert.Equal(1, _backup.Runs);
        Assert.Equal(0, _retention.Runs);
        Assert.Equal((TaskRunEnd.Done, "backup done"), (outcome.End, outcome.Result!.Body));
    }

    [Fact]
    public async Task Runner_JobFailsOrCrashes_DoneWithTheFailureSoTheScheduleContinues()
    {
        _backup.Failure = new Error("backup.disk_full", "No space left.", ErrorType.Failure);
        TaskRunOutcome failed = await Runner().RunAsync(SystemTask("backup"), new RunLimits(), Ct);
        _retention.Crash = true;
        TaskRunOutcome crashed = await Runner().RunAsync(SystemTask("retention"), new RunLimits(), Ct);

        Assert.Equal((TaskRunEnd.Done, "backup failed: No space left."), (failed.End, failed.Result!.Speech));
        Assert.Equal((TaskRunEnd.Done, "retention failed: Boom."), (crashed.End, crashed.Result!.Speech));
    }

    [Fact]
    public async Task Runner_UnknownJob_Failed()
    {
        TaskRunOutcome outcome = await Runner().RunAsync(SystemTask("defragment"), new RunLimits(), Ct);

        Assert.Equal((TaskRunEnd.Failed, "unknown_job"), (outcome.End, outcome.Reason));
    }

    private static TaskRecord SystemTask(string job) => new()
    {
        Id = TaskId.New(),
        OwnerId = OwnerA,
        Kind = TaskKind.System,
        Job = job,
        Goal = "System work.",
        Trigger = TaskTrigger.Cron("0 3 * * *"),
        CreatedBy = TaskCreator.System,
        Status = AgentTaskStatus.Running,
        Budget = new TaskBudget(),
        Spent = TaskSpend.Zero,
        Deliveries = [],
        Depth = 0,
        RowVersion = 0,
        CreatedAt = DateTimeOffset.UnixEpoch,
        UpdatedAt = DateTimeOffset.UnixEpoch,
    };

    private SystemTaskRunner Runner() => new([_backup, _retention], NullLogger<SystemTaskRunner>.Instance);

    private SystemTaskSeeder Seeder(MaintenanceOptions? options = null) => new(
        new FixedOwner(),
        Tasks(),
        new ViennaProfiles(),
        [_backup, _retention],
        _database.Accessor,
        Options.Create(options ?? new MaintenanceOptions()),
        _time,
        NullLogger<SystemTaskSeeder>.Instance);

    private EfTaskStore Tasks() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private sealed class FixedOwner : IInstanceOwner
    {
        public Task<OwnerId> GetAsync(CancellationToken cancellationToken) => Task.FromResult(OwnerA);
    }

    private sealed class StubJob(string name) : ISystemJob
    {
        public int Runs { get; private set; }

        public Error? Failure { get; set; }

        public bool Crash { get; set; }

        public string Name => name;

        public Task<Result<string>> RunAsync(CancellationToken cancellationToken)
        {
            Runs++;
            if (Crash)
                throw new InvalidOperationException("Boom.");
            return Task.FromResult(Failure is { } failure ? Result.Failure<string>(failure) : Result.Success($"{name} done"));
        }
    }
}
