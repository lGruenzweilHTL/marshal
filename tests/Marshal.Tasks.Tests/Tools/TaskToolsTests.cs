using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Data.Identity;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Limits;
using Marshal.Tasks.Running;
using Marshal.Tasks.Store;
using Marshal.Tasks.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Tools;

public sealed class TaskToolsTests : IAsyncLifetime
{
    // 2026-09-29 10:00 UTC = 12:00 in Vienna.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly TaskOptions _options = new() { MaxDepth = 1 };
    private readonly FakeAuditLog _audit = new();
    private readonly RecordingScheduler _scheduler = new();
    private readonly ActiveTaskRuns _runs = new();
    private readonly FakeTemplateCatalog _templates = new();
    private Marshal.Tasks.Policies.ITaskToolPolicy? _policy;
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Descriptors_TiersFlagsAndConfirmingArguments()
    {
        IReadOnlyList<ITool> tools = await Source().GetToolsAsync(Ct);

        Assert.Equal(
            ["tasks.cancel", "tasks.create", "tasks.list", "tasks.run_now", "tasks.schedule_wakeup", "tasks.status", "tasks.stop_all", "tasks.update", "tasks.watch"],
            tools.Select(tool => tool.Descriptor.Id.ToString()).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier));
        Assert.Equal(["tasks.create", "tasks.schedule_wakeup", "tasks.update", "tasks.watch"], tools.Where(tool => tool.Descriptor.Persists).Select(tool => tool.Descriptor.Id.ToString()).Order(StringComparer.Ordinal));
        Assert.Equal(["tasks.create", "tasks.update", "tasks.watch"], tools.Where(tool => tool.Descriptor.ConfirmingArguments.TryGetValue("overrideQuietHoursUntil", out ConfirmationReasonCode code) && code == ConfirmationReasonCode.QuietHoursOverride).Select(tool => tool.Descriptor.Id.ToString()).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Create_InATurnThatLoadedPrinter_PolicyHasCoreAndPrinterTools()
    {
        await UsePoliciesAsync();

        await InvokeAsync("create", new { goal = "Watch the print.", name = "print" }, Loaded(Text(OwnerA), "printer"));

        Assert.Equal(["agent.*", "memory.*", "notifications.*", "printer.*", "tasks.*"], (await Store().FindAsync("print", Ct)).Value.ToolPolicy);
    }

    [Fact]
    public async Task Create_WithTools_NarrowsThePolicy()
    {
        await UsePoliciesAsync();

        await InvokeAsync("create", new { goal = "Watch the print.", name = "print", tools = new[] { "printer.status", "notifications.notify" } }, Loaded(Text(OwnerA)));

        Assert.Equal(["printer.status", "notifications.notify"], (await Store().FindAsync("print", Ct)).Value.ToolPolicy);
    }

    [Fact]
    public async Task Create_WithToolsOutsideTheTemplate_FailsAndCreatesNothing()
    {
        await UsePoliciesAsync();
        _templates.Items.Add(FakeTemplateCatalog.Template("printing", ["printer.*", "notifications.*"]));

        Result<ToolOutput> result = await InvokeAsync("create", new { goal = "Read my mail.", template = "printing", tools = new[] { "mail.*" } }, Loaded(Text(OwnerA)));

        Assert.Equal((ErrorType.Validation, "tasks.invalid_tools"), (result.Error.Type, result.Error.Code));
        Assert.Empty(await Store().ListAsync(new TaskFilter(), Ct));
    }

    [Fact]
    public async Task Watch_StoresThePolicy()
    {
        await UsePoliciesAsync();

        await InvokeAsync("watch", new { filter = new { source = "printer" }, goal = "Tell me when it is done.", name = "done", tools = new[] { "notifications.notify" } }, Loaded(Text(OwnerA)));

        Assert.Equal(["notifications.notify"], (await Store().FindAsync("done", Ct)).Value.ToolPolicy);
    }

    [Fact]
    public async Task Update_WithTools_FailsNamingTools()
    {
        await UsePoliciesAsync();
        await InvokeAsync("create", new { goal = "Watch the print.", name = "print", tools = new[] { "printer.status" } }, Loaded(Text(OwnerA)));

        Result<ToolOutput> result = await InvokeAsync("update", new { task = "print", tools = new[] { "mail.search" } });

        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Contains("tools", result.Error.Message, StringComparison.Ordinal);
        Assert.Equal(["printer.status"], (await Store().FindAsync("print", Ct)).Value.ToolPolicy);
    }

    [Fact]
    public async Task Create_ConfirmationTextListsThePolicy()
    {
        await UsePoliciesAsync();
        ITool create = await ToolAsync("create");

        string summary = create.Summarize(JsonSerializer.SerializeToElement(new { goal = "Watch the print.", tools = new[] { "printer.status" } }), Loaded(Text(OwnerA)));
        string derived = create.Summarize(JsonSerializer.SerializeToElement(new { goal = "Watch the print." }), Loaded(Text(OwnerA), "printer"));

        Assert.Contains("Watch the print.", summary, StringComparison.Ordinal);
        Assert.Contains("tools: printer.status", summary, StringComparison.Ordinal);
        Assert.Contains("printer.*", derived, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_ReminderAtLocalTimeFromASatellite()
    {
        ToolOutput output = (await InvokeAsync("create", new { goal = "Remind me to call mum.", trigger = new { kind = "at", time = "2026-09-30T07:00" }, name = "Call-Mum" }, Voice(OwnerA))).Value;

        TaskRecord task = (await Store().FindAsync("call-mum", Ct)).Value;
        Assert.Equal((new DateTimeOffset(2026, 9, 30, 5, 0, 0, TimeSpan.Zero), "assist_satellite.kitchen", TaskCreator.User, (TaskId?)null), (task.NextRunAt, task.OriginSatelliteId, task.CreatedBy, task.ParentId));
        Assert.Contains("2026-09-30T07:00:00+02:00", output.Content, StringComparison.Ordinal);
        Assert.Contains(task.Id.ToString(), output.Content, StringComparison.Ordinal);
        Assert.True(_scheduler.Woken > 0);
    }

    [Fact]
    public async Task Create_InATaskRun_IsAChildWithTheRunsProject()
    {
        TaskRecord parent = await CreateAsync("Research printers.");
        var project = new ProjectId(Guid.CreateVersion7());
        await using (Marshal.Data.MarshalDbContext db = _database.CreateContext())
        {
            db.Projects.Add(new Marshal.Data.Memory.Project { Id = project, OwnerId = OwnerA, Name = "Voron", Description = "", CreatedAt = _time.GetUtcNow() });
            await db.SaveChangesAsync(Ct);
        }

        await InvokeAsync("create", new { goal = "Compare prices.", name = "prices" }, TaskRun(parent) with { ProjectId = project });

        TaskRecord child = (await Store().FindAsync("prices", Ct)).Value;
        Assert.Equal((parent.Id, TaskCreator.Task, 1, (ProjectId?)project), (child.ParentId, child.CreatedBy, child.Depth, child.ProjectId));
    }

    [Fact]
    public async Task Create_WithDeliveries_Stored()
    {
        await InvokeAsync("create", new
        {
            goal = "Briefing.",
            name = "briefing",
            trigger = new { kind = "cron", expression = "0 6 * * *" },
            deliver = new object[]
            {
                new { channel = "voice", followUp = true, when = new[] { "state(binary_sensor.kitchen_occupancy = on)" }, expires = "11:00", fallback = "push" },
                new { channel = "inbox", priority = "low" },
            },
        });

        TaskRecord task = (await Store().FindAsync("briefing", Ct)).Value;
        Assert.Equal(2, task.Deliveries.Count);
        TaskDelivery voice = task.Deliveries[0];
        Assert.Equal((DeliveryChannel.Voice, true, "11:00", (DeliveryChannel?)DeliveryChannel.Push), (voice.Channel, voice.FollowUp, voice.Expires, voice.Fallback));
        Assert.Equal(["state(binary_sensor.kitchen_occupancy = on)"], voice.When);
        Assert.Equal((DeliveryChannel.Inbox, NotificationPriority.Low), (task.Deliveries[1].Channel, task.Deliveries[1].Priority));
    }

    [Theory]
    [InlineData("3h")]
    [InlineData("180m")]
    [InlineData("PT3H")]
    [InlineData("03:00:00")]
    public async Task Create_WithPreparation_Stored(string before)
    {
        Result<ToolOutput> created = await InvokeAsync("create", new
        {
            goal = "Briefing.",
            name = "briefing",
            trigger = new { kind = "cron", expression = "0 6 * * *" },
            prepare = new { before, goal = "Collect today's appointments." },
        });

        Assert.True(created.IsSuccess);
        TaskRecord task = (await Store().FindAsync("briefing", Ct)).Value;
        Assert.Equal(new TaskPreparation(TimeSpan.FromHours(3), "Collect today's appointments."), task.Preparation);
        Assert.Contains("\"prepare\":{\"before\":\"03:00\"", created.Value.Content, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("""{"goal":"x","trigger":{"kind":"cron","expression":"0 6 * * *"},"prepare":{"before":"13h","goal":"y"}}""")]
    [InlineData("""{"goal":"x","trigger":{"kind":"cron","expression":"0 6 * * *"},"prepare":{"before":"soon","goal":"y"}}""")]
    [InlineData("""{"goal":"x","trigger":{"kind":"cron","expression":"0 6 * * *"},"prepare":{"before":"3h"}}""")]
    [InlineData("""{"goal":"x","trigger":{"kind":"now"},"prepare":{"before":"3h","goal":"y"}}""")]
    public async Task Create_InvalidPreparation_ValidationErrorNothingStored(string arguments)
    {
        Result<ToolOutput> created = await InvokeAsync("create", JsonDocument.Parse(arguments).RootElement);

        Assert.Equal(ErrorType.Validation, created.Error.Type);
        Assert.Empty(await Store().ListAsync(new TaskFilter(), Ct));
    }

    [Fact]
    public async Task Update_SetsAndRemovesThePreparation()
    {
        await InvokeAsync("create", new { goal = "Briefing.", name = "briefing", trigger = new { kind = "cron", expression = "0 6 * * *" } });

        await InvokeAsync("update", new { task = "briefing", prepare = new { before = "2h", goal = "Collect." } });
        TaskRecord prepared = (await Store().FindAsync("briefing", Ct)).Value;
        await InvokeAsync("update", JsonDocument.Parse("""{"task":"briefing","prepare":null}""").RootElement);
        TaskRecord removed = (await Store().FindAsync("briefing", Ct)).Value;

        Assert.Equal(new TaskPreparation(TimeSpan.FromHours(2), "Collect."), prepared.Preparation);
        Assert.Equal(new DateTimeOffset(2026, 9, 30, 2, 0, 0, TimeSpan.Zero), prepared.NextPrepareAt);
        Assert.Equal(((TaskPreparation?)null, (DateTimeOffset?)null), (removed.Preparation, removed.NextPrepareAt));
    }

    [Theory]
    [InlineData("""{"goal":"x","trigger":{"kind":"cron","expression":"not cron"}}""")]
    [InlineData("""{"goal":"x","trigger":{"kind":"at","time":"tomorrow"}}""")]
    [InlineData("""{"goal":"x","trigger":{"kind":"sometime"}}""")]
    [InlineData("""{"goal":"x","deliver":[{"channel":"telepathy"}]}""")]
    [InlineData("""{"goal":"x","deliver":[{"channel":"push","priority":"critical"}]}""")]
    [InlineData("""{"trigger":{"kind":"now"}}""")]
    public async Task Create_Invalid_ValidationErrorNothingStored(string arguments)
    {
        Result<ToolOutput> created = await InvokeAsync("create", JsonDocument.Parse(arguments).RootElement);

        Assert.Equal(ErrorType.Validation, created.Error.Type);
        Assert.Empty(await Store().ListAsync(new TaskFilter(), Ct));
    }

    [Fact]
    public async Task Create_HiddenWhileTheKillSwitchIsActive()
    {
        await KillSwitch().ActivateAsync(Ct);

        Assert.False(await AvailableAsync("create", Text(OwnerA)));
        Assert.False(await AvailableAsync("watch", Text(OwnerA)));
        Assert.False(await AvailableAsync("run_now", Text(OwnerA)));
        Assert.True(await AvailableAsync("list", Text(OwnerA)));
    }

    [Fact]
    public async Task Create_HiddenInARunAtMaxDepth()
    {
        TaskRecord root = await CreateAsync("Root.");
        TaskRecord child = (await Store().CreateAsync(new NewTask { Goal = "Child.", Trigger = TaskTrigger.Now(), ParentId = root.Id }, Ct)).Value;

        Assert.True(await AvailableAsync("create", TaskRun(root)));
        Assert.False(await AvailableAsync("create", TaskRun(child)));
    }

    [Fact]
    public async Task ScheduleWakeup_OnlyInTaskRuns_EndsTheRunWaiting()
    {
        TaskRecord task = await CreateAsync("Watch the print.");
        using ActiveTaskRuns.Scope run = _runs.Begin(task.Id);

        Result<ToolOutput> woken = await InvokeAsync("schedule_wakeup", new { inMinutes = 30, notes = "40 % at 12:00." }, TaskRun(task));

        Assert.True(woken.IsSuccess);
        Assert.Equal((true, _time.GetUtcNow().AddMinutes(30), "40 % at 12:00."), (run.State.WaitRequested, run.State.WakeAt, run.State.Notes));
        Assert.False(await AvailableAsync("schedule_wakeup", Text(OwnerA)));
        Assert.True(await AvailableAsync("schedule_wakeup", TaskRun(task)));
    }

    [Fact]
    public async Task ScheduleWakeup_TooSoon_ValidationError()
    {
        TaskRecord task = await CreateAsync("Watch the print.");
        using ActiveTaskRuns.Scope run = _runs.Begin(task.Id);

        Result<ToolOutput> woken = await InvokeAsync("schedule_wakeup", new { inMinutes = 1 }, TaskRun(task));

        Assert.Equal(ErrorType.Validation, woken.Error.Type);
        Assert.False(run.State.WaitRequested);
    }

    [Fact]
    public async Task Watch_CreatesAnEventTask()
    {
        await InvokeAsync("watch", new { filter = new { source = "ha", entity = "person.alice", to = "home" }, goal = "Remind me of the parcel.", name = "parcel" });

        TaskRecord watch = (await Store().FindAsync("parcel", Ct)).Value;
        Assert.Equal(TaskTriggerKind.Event, watch.Trigger.Kind);
        using var filter = JsonDocument.Parse(watch.Trigger.Spec);
        Assert.Equal("person.alice", filter.RootElement.GetProperty("entity").GetString());
        Assert.Null(watch.NextRunAt);
    }

    [Fact]
    public async Task Watch_Once_CreatesAOnceWatchShownInListAndStatus()
    {
        await InvokeAsync("watch", new { filter = new { source = "ha", entity = "person.alice", to = "Hornbach" }, goal = "Remind me to buy wall plugs.", name = "plugs", once = true });

        TaskRecord watch = (await Store().FindAsync("plugs", Ct)).Value;
        Assert.Equal((TaskTriggerKind.Event, true), (watch.Trigger.Kind, watch.Trigger.Once));
        using var status = JsonDocument.Parse((await InvokeAsync("status", new { task = "plugs" })).Value.Content);
        Assert.True(status.RootElement.GetProperty("once").GetBoolean());
        using var list = JsonDocument.Parse((await InvokeAsync("list", new { })).Value.Content);
        Assert.True(Assert.Single(list.RootElement.EnumerateArray()).GetProperty("once").GetBoolean());
    }

    [Fact]
    public async Task Watch_OnceNotABoolean_ValidationErrorAndNoTask()
    {
        Result<ToolOutput> watched = await InvokeAsync("watch", new { filter = new { source = "ha" }, goal = "x", name = "plugs", once = "yes" });

        Assert.Equal(ErrorType.Validation, watched.Error.Type);
        Assert.True((await Store().FindAsync("plugs", Ct)).IsFailure);
    }

    [Fact]
    public async Task Watch_FilterWithoutSource_ValidationError()
    {
        Assert.Equal(ErrorType.Validation, (await InvokeAsync("watch", new { filter = new { entity = "person.alice" }, goal = "x" })).Error.Type);
    }

    [Fact]
    public async Task Update_ByNameChangesGoalAndSchedule()
    {
        await InvokeAsync("create", new { goal = "Briefing.", name = "briefing", trigger = new { kind = "cron", expression = "0 6 * * *" } });

        await InvokeAsync("update", new { task = "briefing", goal = "Short briefing.", trigger = new { kind = "cron", expression = "30 7 * * *" } });

        TaskRecord task = (await Store().FindAsync("briefing", Ct)).Value;
        Assert.Equal(("Short briefing.", "30 7 * * *", new DateTimeOffset(2026, 9, 30, 5, 30, 0, TimeSpan.Zero)), (task.Goal, task.Trigger.Spec, task.NextRunAt));
    }

    [Theory]
    [InlineData("budget")]
    [InlineData("owner")]
    [InlineData("originSatellite")]
    public async Task Update_OwnerBudgetOrOriginSatellite_Rejected(string property)
    {
        ITool update = await ToolAsync("update");

        Result valid = update.Validate(JsonDocument.Parse($$"""{"task":"briefing","{{property}}":"x"}""").RootElement);

        Assert.Equal(ErrorType.Validation, valid.Error.Type);
    }

    [Fact]
    public async Task Update_HiddenWhenNoTasksExist()
    {
        Assert.False(await AvailableAsync("update", Text(OwnerA)));
        await CreateAsync("Something.");
        Assert.True(await AvailableAsync("update", Text(OwnerA)));
    }

    [Fact]
    public async Task RunNow_DoneTaskBecomesDueNow()
    {
        TaskRecord task = await CreateAsync("Briefing.", "briefing");
        await Store().UpdateAsync(task with { Status = AgentTaskStatus.Done, NextRunAt = null }, Ct);

        await InvokeAsync("run_now", new { task = "briefing" });

        TaskRecord due = (await Store().GetAsync(task.Id, Ct)).Value;
        Assert.Equal((AgentTaskStatus.Pending, _time.GetUtcNow()), (due.Status, due.NextRunAt));
        Assert.True(_scheduler.Woken > 0);
    }

    [Fact]
    public async Task RunNow_Running_Conflict()
    {
        TaskRecord task = await CreateAsync("Briefing.", "briefing");
        await Store().UpdateAsync(task with { Status = AgentTaskStatus.Running }, Ct);

        Assert.Equal(ErrorType.Conflict, (await InvokeAsync("run_now", new { task = "briefing" })).Error.Type);
    }

    [Fact]
    public async Task Status_ByName_LatestResultWithItsAge()
    {
        TaskRecord task = await CreateAsync("Briefing.", "briefing");
        await Store().UpdateAsync(task with { Status = AgentTaskStatus.Done, Result = new TaskResult("Three new mails.", "**Three** new mails."), ResultAt = _time.GetUtcNow().AddMinutes(-90) }, Ct);

        using var status = JsonDocument.Parse((await InvokeAsync("status", new { task = "briefing" })).Value.Content);

        JsonElement result = status.RootElement.GetProperty("result");
        Assert.Equal(("Three new mails.", "**Three** new mails.", 90), (result.GetProperty("speech").GetString(), result.GetProperty("body").GetString(), result.GetProperty("ageMinutes").GetInt32()));
        Assert.Equal("done", status.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Status_OtherOwnersTask_NotFound()
    {
        _database.Accessor.Current = Text(OwnerB);
        await CreateAsync("Secret.", "secret");
        _database.Accessor.Current = Text(OwnerA);

        Assert.Equal("tasks.not_found", (await InvokeAsync("status", new { task = "secret" })).Error.Code);
    }

    [Fact]
    public async Task List_TheOwnersTasks()
    {
        await CreateAsync("One.", "one");
        await CreateAsync("Two.", "two");

        using var list = JsonDocument.Parse((await InvokeAsync("list", new { })).Value.Content);

        Assert.Equal(["one", "two"], list.RootElement.EnumerateArray().Select(task => task.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Cancel_ByName_CancelsTheTaskItsChildrenAndTheirRuns()
    {
        TaskRecord task = await CreateAsync("Research.", "research");
        TaskRecord child = (await Store().CreateAsync(new NewTask { Goal = "Child.", Trigger = TaskTrigger.Now(), ParentId = task.Id }, Ct)).Value;

        await InvokeAsync("cancel", new { task = "research" });

        Assert.Equal(AgentTaskStatus.Cancelled, (await Store().GetAsync(task.Id, Ct)).Value.Status);
        Assert.Equal(AgentTaskStatus.Cancelled, (await Store().GetAsync(child.Id, Ct)).Value.Status);
        Assert.Contains(task.Id, _scheduler.Cancelled);
        Assert.Contains(child.Id, _scheduler.Cancelled);
    }

    [Fact]
    public async Task StopAll_CancelsEverythingAndIsAudited()
    {
        TaskRecord task = await CreateAsync("Research.");

        ToolOutput output = (await InvokeAsync("stop_all", new { })).Value;

        Assert.Equal(AgentTaskStatus.Cancelled, (await Store().GetAsync(task.Id, Ct)).Value.Status);
        Assert.Equal("tasks.stop_all", Assert.Single(_audit.Entries).Action);
        Assert.Contains("1", output.Content, StringComparison.Ordinal);
    }

    private EfTaskStore Store() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(_options), _time);

    private EfKillSwitch KillSwitch() =>
        new(_database.Factory, Store(), new EfUserPreferencesStore(_database.Factory, new FixedInstanceTimeZone()), _scheduler, _audit, _database.Accessor, _time);

    private TaskToolSource Source() => new(
        Store(),
        _scheduler,
        KillSwitch(),
        new TaskBudgets(Store(), new NoSpending(), new ViennaProfiles(), _templates, Options.Create(_options), _time),
        _runs,
        new ViennaProfiles(),
        Options.Create(_options),
        _time,
        _policy);

    private async Task UsePoliciesAsync()
    {
        var registry = new Marshal.Core.Tools.ToolRegistry([new FakeToolSource("test", true,
            new FakeTool("agent.delegate"), new FakeTool("memory.recall"), new FakeTool("tasks.create"), new FakeTool("notifications.notify"),
            new FakeTool("printer.status"), new FakeTool("mail.search"))]);
        await registry.RefreshAsync(Ct);
        _policy = new Marshal.Tasks.Policies.DefaultTaskToolPolicy(registry, _templates, Options.Create(new Marshal.Core.Tools.ToolsOptions { Core = ["agent", "memory", "tasks", "notifications"] }));
    }

    private static MarshalExecutionContext Loaded(MarshalExecutionContext context, params string[] namespaces)
    {
        context.LoadedTools.RetrievalActive = true;
        foreach (string name in namespaces)
            context.LoadedTools.Load(name, "retrieval");
        return context;
    }

    private async Task<ITool> ToolAsync(string name) => (await Source().GetToolsAsync(Ct)).Single(tool => tool.Descriptor.Id.Name == name);

    private Task<Result<ToolOutput>> InvokeAsync(string name, object arguments, MarshalExecutionContext? context = null) =>
        InvokeAsync(name, JsonSerializer.SerializeToElement(arguments), context);

    private async Task<Result<ToolOutput>> InvokeAsync(string name, JsonElement arguments, MarshalExecutionContext? context = null)
    {
        MarshalExecutionContext run = context ?? Text(OwnerA);
        using (_database.Accessor.Use(run))
            return await (await ToolAsync(name)).InvokeAsync(new ToolInvocation(arguments, run), Ct);
    }

    private async Task<bool> AvailableAsync(string name, MarshalExecutionContext context)
    {
        using (_database.Accessor.Use(context))
            return await (await ToolAsync(name)).IsAvailableAsync(context, Ct);
    }

    private async Task<TaskRecord> CreateAsync(string goal, string? name = null) =>
        (await Store().CreateAsync(new NewTask { Goal = goal, Trigger = TaskTrigger.At(_time.GetUtcNow().AddHours(1)), Name = name }, Ct)).Value;

    private static MarshalExecutionContext TaskRun(TaskRecord task) =>
        Text(task.OwnerId) with { Source = SessionSource.Task, IsInteractive = false, TaskId = task.Id };

    private sealed class NoSpending : ITaskSpending
    {
        public Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(0m);
    }

    private sealed class RecordingScheduler : ITaskScheduler
    {
        public List<TaskId> Cancelled { get; } = [];

        public int Woken { get; private set; }

        public void Wake() => Woken++;

        public void CancelRun(TaskId task) => Cancelled.Add(task);
    }
}
