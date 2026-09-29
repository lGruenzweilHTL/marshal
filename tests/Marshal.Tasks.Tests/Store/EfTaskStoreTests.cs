using Marshal.Abstractions;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Store;

public sealed class EfTaskStoreTests : IAsyncLifetime
{
    // 2026-09-29 10:00 UTC = 12:00 in Vienna (summer time).
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly TaskOptions _options = new() { MaxDepth = 2, MaxActiveChildren = 2 };
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Create_CronTask_NextRunInOwnersTimeZone()
    {
        TaskRecord task = (await Store().CreateAsync(New("briefing", TaskTrigger.Cron("0 6 * * *")), Ct)).Value;

        Assert.Equal((OwnerA, AgentTaskStatus.Pending, 0, 0L), (task.OwnerId, task.Status, task.Depth, task.RowVersion));
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 4, 0, 0, TimeSpan.Zero), task.NextRunAt);
        TaskRecord stored = (await Store().GetAsync(task.Id, Ct)).Value;
        Assert.Equal((task.Id, "briefing", task.Trigger, task.NextRunAt, task.Budget, task.Spent), (stored.Id, stored.Name, stored.Trigger, stored.NextRunAt, stored.Budget, stored.Spent));
    }

    [Fact]
    public async Task Create_NowAtEventAfter_NextRun()
    {
        DateTimeOffset at = _time.GetUtcNow().AddHours(2);

        TaskRecord now = (await Store().CreateAsync(New(null, TaskTrigger.Now()), Ct)).Value;
        TaskRecord later = (await Store().CreateAsync(New(null, TaskTrigger.At(at)), Ct)).Value;
        TaskRecord onEvent = (await Store().CreateAsync(New(null, TaskTrigger.Event("""{"source":"ha","entity":"person.alice","to":"home"}""")), Ct)).Value;
        TaskRecord after = (await Store().CreateAsync(New(null, TaskTrigger.After(now.Id)), Ct)).Value;

        Assert.Equal(_time.GetUtcNow(), now.NextRunAt);
        Assert.Equal(at, later.NextRunAt);
        Assert.Null(onEvent.NextRunAt);
        Assert.Null(after.NextRunAt);
    }

    [Fact]
    public async Task Create_DuplicateNameForSameOwner_ValidationError()
    {
        await Store().CreateAsync(New("briefing"), Ct);

        Result<TaskRecord> second = await Store().CreateAsync(New("Briefing"), Ct);

        Assert.Equal(("tasks.name_taken", ErrorType.Validation), (second.Error.Code, second.Error.Type));
    }

    [Fact]
    public async Task Create_SameNameForAnotherOwner_Allowed()
    {
        await Store().CreateAsync(New("briefing"), Ct);
        _database.Accessor.Current = Text(OwnerB);

        Assert.True((await Store().CreateAsync(New("briefing"), Ct)).IsSuccess);
    }

    public static TheoryData<string, string, string?> InvalidTasks => new()
    {
        { "empty goal", "cron", null },
        { "Check mail.", "cron:not a cron", null },
        { "Check mail.", "event:[1,2]", null },
        { "Check mail.", "event:not json", null },
        { "Check mail.", "at:yesterday", null },
        { "Check mail.", "after:unknown", null },
        { "Check mail.", "now", "has spaces" },
        { "Check mail.", "now", "0190b4a2-0000-7000-8000-000000000000" },
    };

    [Theory]
    [MemberData(nameof(InvalidTasks))]
    public async Task Create_Invalid_ValidationErrorAndNothingStored(string goal, string trigger, string? name)
    {
        TaskTrigger parsed = trigger.Split(':', 2) switch
        {
            ["cron"] => TaskTrigger.Cron("0 6 * * *"),
            ["cron", string spec] => TaskTrigger.Cron(spec),
            ["event", string spec] => TaskTrigger.Event(spec),
            ["at", string spec] => new TaskTrigger(TaskTriggerKind.At, spec),
            ["after", string spec] => new TaskTrigger(TaskTriggerKind.After, spec),
            _ => TaskTrigger.Now(),
        };

        Result<TaskRecord> created = await Store().CreateAsync(New(name, parsed) with { Goal = goal == "empty goal" ? " " : goal }, Ct);

        Assert.Equal(ErrorType.Validation, created.Error.Type);
        Assert.Empty(await Store().ListAsync(new TaskFilter(), Ct));
    }

    [Fact]
    public async Task Create_AfterAnotherOwnersTask_ValidationError()
    {
        _database.Accessor.Current = Text(OwnerB);
        TaskRecord foreign = (await Store().CreateAsync(New(null), Ct)).Value;
        _database.Accessor.Current = Text(OwnerA);

        Assert.Equal(ErrorType.Validation, (await Store().CreateAsync(New(null, TaskTrigger.After(foreign.Id)), Ct)).Error.Type);
    }

    [Fact]
    public async Task Create_Child_DepthFromParent()
    {
        TaskRecord parent = (await Store().CreateAsync(New(null), Ct)).Value;

        TaskRecord child = (await Store().CreateAsync(New(null) with { ParentId = parent.Id, CreatedBy = TaskCreator.Task }, Ct)).Value;

        Assert.Equal((parent.Id, 1), (child.ParentId, child.Depth));
        Assert.Equal([child.Id], (await Store().ListAsync(new TaskFilter(ParentId: parent.Id), Ct)).Select(task => task.Id));
    }

    [Fact]
    public async Task Create_Child_KeepsTheParentsOriginSatellite()
    {
        TaskRecord parent = (await Store().CreateAsync(New(null) with { OriginSatelliteId = "assist_satellite.kitchen" }, Ct)).Value;

        TaskRecord child = (await Store().CreateAsync(New(null) with { ParentId = parent.Id, OriginSatelliteId = "assist_satellite.office" }, Ct)).Value;
        TaskRecord grandchild = (await Store().CreateAsync(New(null) with { ParentId = child.Id }, Ct)).Value;

        Assert.Equal(("assist_satellite.kitchen", "assist_satellite.kitchen", 2), (child.OriginSatelliteId, grandchild.OriginSatelliteId, grandchild.Depth));
    }

    [Fact]
    public async Task Create_BeyondMaxDepth_ValidationError()
    {
        TaskRecord root = (await Store().CreateAsync(New(null), Ct)).Value;
        TaskRecord child = (await Store().CreateAsync(New(null) with { ParentId = root.Id }, Ct)).Value;
        TaskRecord grandchild = (await Store().CreateAsync(New(null) with { ParentId = child.Id }, Ct)).Value;

        Result<TaskRecord> tooDeep = await Store().CreateAsync(New(null) with { ParentId = grandchild.Id }, Ct);

        Assert.Equal(2, grandchild.Depth);
        Assert.Equal(("tasks.invalid", ErrorType.Validation), (tooDeep.Error.Code, tooDeep.Error.Type));
    }

    [Fact]
    public async Task Create_TooManyActiveChildren_ValidationErrorUntilOneEnds()
    {
        TaskRecord parent = (await Store().CreateAsync(New(null), Ct)).Value;
        TaskRecord first = (await Store().CreateAsync(New(null) with { ParentId = parent.Id }, Ct)).Value;
        await Store().CreateAsync(New(null) with { ParentId = parent.Id }, Ct);

        Result<TaskRecord> third = await Store().CreateAsync(New(null) with { ParentId = parent.Id }, Ct);
        await Store().UpdateAsync(first with { Status = AgentTaskStatus.Done }, Ct);
        Result<TaskRecord> afterOneEnded = await Store().CreateAsync(New(null) with { ParentId = parent.Id }, Ct);

        Assert.Equal("tasks.invalid", third.Error.Code);
        Assert.True(afterOneEnded.IsSuccess);
    }

    [Fact]
    public async Task Create_CronMoreOftenThanTheMinimumWakeupInterval_ValidationError()
    {
        Result<TaskRecord> everyMinute = await Store().CreateAsync(New(null, TaskTrigger.Cron("* * * * *")), Ct);
        Result<TaskRecord> everyFive = await Store().CreateAsync(New(null, TaskTrigger.Cron("*/5 * * * *")), Ct);

        Assert.Equal("tasks.invalid", everyMinute.Error.Code);
        Assert.True(everyFive.IsSuccess);
    }

    [Fact]
    public async Task Create_UnknownOrForeignProject_ValidationError()
    {
        var foreign = new Marshal.Abstractions.Memory.ProjectId(Guid.CreateVersion7());
        await using (Marshal.Data.MarshalDbContext db = _database.CreateContext(OwnerB))
        {
            db.Projects.Add(new Marshal.Data.Memory.Project { Id = foreign, OwnerId = OwnerB, Name = "Bob's", Description = "", CreatedAt = _time.GetUtcNow() });
            await db.SaveChangesAsync(Ct);
        }

        _database.Accessor.Current = Text(OwnerA);

        Assert.Equal("tasks.invalid", (await Store().CreateAsync(New(null) with { ProjectId = foreign }, Ct)).Error.Code);
    }

    [Fact]
    public async Task Create_ParentOfAnotherOwner_NotFound()
    {
        _database.Accessor.Current = Text(OwnerB);
        TaskRecord foreign = (await Store().CreateAsync(New(null), Ct)).Value;
        _database.Accessor.Current = Text(OwnerA);

        Result<TaskRecord> child = await Store().CreateAsync(New(null) with { ParentId = foreign.Id }, Ct);

        Assert.Equal("tasks.not_found", child.Error.Code);
    }

    [Fact]
    public async Task Find_ByIdOrName_OnlyOwnTasks()
    {
        TaskRecord task = (await Store().CreateAsync(New("briefing"), Ct)).Value;

        Assert.Equal(task.Id, (await Store().FindAsync("Briefing", Ct)).Value.Id);
        Assert.Equal(task.Id, (await Store().FindAsync(task.Id.ToString(), Ct)).Value.Id);

        _database.Accessor.Current = Text(OwnerB);
        Assert.Equal("tasks.not_found", (await Store().FindAsync("briefing", Ct)).Error.Code);
        Assert.Equal("tasks.not_found", (await Store().FindAsync(task.Id.ToString(), Ct)).Error.Code);
        Assert.Equal("tasks.not_found", (await Store().GetAsync(task.Id, Ct)).Error.Code);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        TaskRecord done = (await Store().CreateAsync(New(null), Ct)).Value;
        await Store().UpdateAsync(done with { Status = AgentTaskStatus.Done }, Ct);
        TaskRecord pending = (await Store().CreateAsync(New(null), Ct)).Value;

        IReadOnlyList<TaskRecord> listed = await Store().ListAsync(new TaskFilter([AgentTaskStatus.Pending, AgentTaskStatus.Waiting]), Ct);

        Assert.Equal([pending.Id], listed.Select(task => task.Id));
    }

    [Fact]
    public async Task Update_ChangesMutableFieldsOnly()
    {
        TaskRecord task = (await Store().CreateAsync(New("briefing") with { Budget = new TaskBudget(Cost: 0.5m), OriginSatelliteId = "kitchen" }, Ct)).Value;
        _time.Advance(TimeSpan.FromMinutes(1));

        TaskRecord updated = (await Store().UpdateAsync(task with
        {
            Goal = "Shorter briefing.",
            Notes = "Skip the weather.",
            Result = new TaskResult("Done.", "**Done.**"),
            Budget = new TaskBudget(Cost: 100m),
            OriginSatelliteId = null,
            Depth = 3,
        }, Ct)).Value;

        TaskRecord stored = (await Store().GetAsync(task.Id, Ct)).Value;
        Assert.Equal((updated.Goal, updated.RowVersion, updated.Result), (stored.Goal, stored.RowVersion, stored.Result));
        Assert.Equal(("Shorter briefing.", "Skip the weather.", "Done.", 1L), (stored.Goal, stored.Notes, stored.Result?.Speech, stored.RowVersion));
        Assert.Equal((0.5m, "kitchen", 0), (stored.Budget.Cost, stored.OriginSatelliteId, stored.Depth));
        Assert.Equal(_time.GetUtcNow(), stored.UpdatedAt);
    }

    [Fact]
    public async Task Update_StaleVersion_Conflict()
    {
        TaskRecord task = (await Store().CreateAsync(New(null), Ct)).Value;
        await Store().UpdateAsync(task with { Notes = "first" }, Ct);

        Result<TaskRecord> stale = await Store().UpdateAsync(task with { Notes = "second" }, Ct);

        Assert.Equal(("tasks.conflict", ErrorType.Conflict), (stale.Error.Code, stale.Error.Type));
        Assert.Equal("first", (await Store().GetAsync(task.Id, Ct)).Value.Notes);
    }

    [Fact]
    public async Task Update_RenameToTakenName_ValidationError()
    {
        await Store().CreateAsync(New("briefing"), Ct);
        TaskRecord other = (await Store().CreateAsync(New("other"), Ct)).Value;

        Assert.Equal("tasks.name_taken", (await Store().UpdateAsync(other with { Name = "briefing" }, Ct)).Error.Code);
    }

    [Fact]
    public async Task Update_OtherOwnersTask_NotFound()
    {
        TaskRecord task = (await Store().CreateAsync(New(null), Ct)).Value;
        _database.Accessor.Current = Text(OwnerB);

        Assert.Equal("tasks.not_found", (await Store().UpdateAsync(task with { Goal = "Hijack." }, Ct)).Error.Code);
    }

    [Fact]
    public async Task DueQueries_SpanOwnersEarliestFirst()
    {
        TaskRecord a = (await Store().CreateAsync(New(null, TaskTrigger.At(_time.GetUtcNow().AddMinutes(10))), Ct)).Value;
        TaskRecord running = (await Store().CreateAsync(New(null), Ct)).Value;
        await Store().UpdateAsync(running with { Status = AgentTaskStatus.Running }, Ct);
        await Store().CreateAsync(New(null, TaskTrigger.At(_time.GetUtcNow().AddHours(5))), Ct);
        _database.Accessor.Current = Text(OwnerB);
        TaskRecord b = (await Store().CreateAsync(New(null, TaskTrigger.At(_time.GetUtcNow().AddMinutes(5))), Ct)).Value;
        _database.Accessor.Current = null;

        IReadOnlyList<OwnedTask> due = await Store().GetDueAsync(_time.GetUtcNow().AddMinutes(30), 10, Ct);

        Assert.Equal([new OwnedTask(b.Id, OwnerB), new OwnedTask(a.Id, OwnerA)], due);
        Assert.Equal(_time.GetUtcNow().AddMinutes(5), await Store().GetNextRunAtAsync(Ct));
        Assert.Equal([new OwnedTask(running.Id, OwnerA)], await Store().GetRunningAsync(Ct));
    }

    [Fact]
    public async Task GetWatches_ArmedEventTasksOfEveryOwner()
    {
        const string Home = """{"source":"ha","entity":"person.alice","to":"home"}""";
        TaskRecord armed = (await Store().CreateAsync(New(null, TaskTrigger.Event(Home)), Ct)).Value;
        TaskRecord fired = (await Store().CreateAsync(New(null, TaskTrigger.Event(Home)), Ct)).Value;
        await Store().UpdateAsync(fired with { NextRunAt = _time.GetUtcNow(), TriggerPayload = "{}" }, Ct);
        TaskRecord cancelled = (await Store().CreateAsync(New(null, TaskTrigger.Event(Home)), Ct)).Value;
        await Store().UpdateAsync(cancelled with { Status = AgentTaskStatus.Cancelled }, Ct);
        await Store().CreateAsync(New(null, TaskTrigger.Cron("0 6 * * *")), Ct);
        _database.Accessor.Current = Text(OwnerB);
        TaskRecord other = (await Store().CreateAsync(New(null, TaskTrigger.Event("""{"source":"printer"}""")), Ct)).Value;
        TaskRecord rearmed = (await Store().CreateAsync(New(null, TaskTrigger.Event("""{"source":"mail"}""")), Ct)).Value;
        await Store().UpdateAsync(rearmed with { Status = AgentTaskStatus.Waiting }, Ct);
        _database.Accessor.Current = null;

        IReadOnlyList<TaskWatch> watches = await Store().GetWatchesAsync(Ct);

        Assert.Equal(
            new[] { new TaskWatch(armed.Id, OwnerA, Home), new TaskWatch(other.Id, OwnerB, """{"source":"printer"}"""), new TaskWatch(rearmed.Id, OwnerB, """{"source":"mail"}""") }.OrderBy(watch => watch.Id.Value),
            watches.OrderBy(watch => watch.Id.Value));
    }

    private EfTaskStore Store() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(_options), _time);

    private static NewTask New(string? name, TaskTrigger? trigger = null) => new()
    {
        Goal = "Prepare the morning briefing.",
        Trigger = trigger ?? TaskTrigger.Cron("0 6 * * *"),
        Name = name,
    };
}
