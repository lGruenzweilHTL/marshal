using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Api.Tests.Auth;
using Marshal.Data.Audit;
using Marshal.Data.Conversations;
using Marshal.Data.Notifications;
using Marshal.Data.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class TasksApiTests : IAsyncLifetime
{
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _factory = new MarshalApiFactory(AuthFactory.Local().ToSettings(0));
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task List_OwnerScopedWithTriggerNextRunBudgetAndResult()
    {
        TaskRecord briefing = await CreateAsync(_user, new NewTask
        {
            Goal = "Morning briefing.",
            Name = "briefing",
            Trigger = TaskTrigger.Cron("0 6 * * *"),
            Budget = new TaskBudget(Cost: 0.5m),
        });
        await UpdateAsync(_user, briefing with { Result = new TaskResult("Two new mails.", "## Mail\n\nTwo new mails."), ResultAt = DateTimeOffset.UtcNow, Spent = new TaskSpend(Cost: 0.12m, Runs: 1) });
        (_, OwnerId other) = await Browser.SignInAsync(_factory, "user");
        await CreateAsync(other, new NewTask { Goal = "Not yours.", Trigger = TaskTrigger.Now() });

        List<JsonElement>? tasks = await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/tasks", Ct);

        JsonElement task = Assert.Single(tasks!);
        Assert.Equal((briefing.Id.ToString(), "briefing", "Morning briefing.", "pending"),
            (task.GetProperty("id").GetString(), task.GetProperty("name").GetString(), task.GetProperty("goal").GetString(), task.GetProperty("status").GetString()));
        Assert.Equal(("cron", "0 6 * * *"), (task.GetProperty("trigger").GetProperty("kind").GetString(), task.GetProperty("trigger").GetProperty("spec").GetString()));
        Assert.Equal(JsonValueKind.String, task.GetProperty("nextRunAt").ValueKind);
        Assert.Equal((0.5m, 0.12m), (task.GetProperty("budget").GetProperty("cost").GetDecimal(), task.GetProperty("spent").GetProperty("cost").GetDecimal()));
        Assert.Equal("Two new mails.", task.GetProperty("resultSpeech").GetString());
    }

    [Fact]
    public async Task List_FilteredByStatus()
    {
        TaskRecord done = await CreateAsync(_user, new NewTask { Goal = "Done already.", Trigger = TaskTrigger.At(DateTimeOffset.UtcNow.AddDays(1)) });
        await UpdateAsync(_user, done with { Status = AgentTaskStatus.Done, NextRunAt = null });
        await CreateAsync(_user, new NewTask { Goal = "Still pending.", Trigger = TaskTrigger.At(DateTimeOffset.UtcNow.AddDays(1)) });

        List<JsonElement>? active = await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/tasks?status=active", Ct);
        List<JsonElement>? finished = await _browser.GetFromJsonAsync<List<JsonElement>>("/api/v1/tasks?status=done", Ct);
        HttpResponseMessage invalid = await _browser.GetAsync("/api/v1/tasks?status=sleeping", Ct);

        Assert.Equal("Still pending.", Assert.Single(active!).GetProperty("goal").GetString());
        Assert.Equal("Done already.", Assert.Single(finished!).GetProperty("goal").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
    }

    [Fact]
    public async Task Detail_GoalNotesResultDeliveriesRunsNotificationsAndExecutorSessions()
    {
        TaskRecord task = await CreateAsync(_user, new NewTask
        {
            Goal = "Check the print.",
            Notes = "Layer 12 looked fine.",
            Trigger = TaskTrigger.At(DateTimeOffset.UtcNow.AddHours(1)),
            Deliveries = [new TaskDelivery(DeliveryChannel.Push)],
        });
        await UpdateAsync(_user, task with { Result = new TaskResult("Print is fine.", "**Print** is fine."), ResultAt = DateTimeOffset.UtcNow });
        var run = RunId.New();
        var session = Guid.NewGuid();
        await _factory.WithDatabaseAsync(_user, async db =>
        {
            db.Runs.Add(new Run { Id = run, OwnerId = _user, TaskId = task.Id, Source = SessionSource.Task, Trigger = "task", StartedAt = DateTimeOffset.UtcNow, Outcome = RunOutcome.Ok });
            db.Runs.Add(new Run { Id = RunId.New(), OwnerId = _user, Source = SessionSource.Web, Trigger = "turn", StartedAt = DateTimeOffset.UtcNow, Outcome = RunOutcome.Ok });
            db.Notifications.Add(new Notification { Id = Guid.NewGuid(), OwnerId = _user, TaskId = task.Id, Source = $"task:{task.Id}", Priority = NotificationPriority.Normal, Content = "Print is fine.", Channel = "push", Outcome = NotificationOutcome.Delivered, CreatedAt = DateTimeOffset.UtcNow });
            db.ExecutorSessions.Add(new ExecutorSession { Id = session, OwnerId = _user, TaskId = task.Id, Executor = "claude_code", Handle = "h-1", Target = "demo", Status = ExecutorSessionStatus.Running, StartedAt = DateTimeOffset.UtcNow });
            return await db.SaveChangesAsync(Ct);
        });

        JsonElement detail = await _browser.GetFromJsonAsync<JsonElement>($"/api/v1/tasks/{task.Id}", Ct);

        Assert.Equal(("Check the print.", "Layer 12 looked fine."), (detail.GetProperty("task").GetProperty("goal").GetString(), detail.GetProperty("notes").GetString()));
        Assert.Equal(("Print is fine.", "**Print** is fine."), (detail.GetProperty("result").GetProperty("speech").GetString(), detail.GetProperty("result").GetProperty("body").GetString()));
        Assert.Equal("push", Assert.Single(detail.GetProperty("deliveries").EnumerateArray()).GetProperty("channel").GetString());
        Assert.Equal(run.ToString(), Assert.Single(detail.GetProperty("runs").EnumerateArray()).GetProperty("id").GetString());
        Assert.Equal("delivered", Assert.Single(detail.GetProperty("notifications").EnumerateArray()).GetProperty("outcome").GetString());
        JsonElement executor = Assert.Single(detail.GetProperty("executorSessions").EnumerateArray());
        Assert.Equal((session.ToString(), "claude_code", "demo", "running"),
            (executor.GetProperty("id").GetString(), executor.GetProperty("executor").GetString(), executor.GetProperty("target").GetString(), executor.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Detail_TriggerOnceForOnceWatchesOnly()
    {
        TaskRecord watch = await CreateAsync(_user, new NewTask { Goal = "Buy wall plugs.", Trigger = TaskTrigger.Event("""{"source":"ha","entity":"person.alice","to":"Hornbach"}""", once: true) });
        TaskRecord cron = await CreateAsync(_user, new NewTask { Goal = "Briefing.", Trigger = TaskTrigger.Cron("0 6 * * *") });

        JsonElement watchDetail = await _browser.GetFromJsonAsync<JsonElement>($"/api/v1/tasks/{watch.Id}", Ct);
        JsonElement cronDetail = await _browser.GetFromJsonAsync<JsonElement>($"/api/v1/tasks/{cron.Id}", Ct);

        Assert.True(watchDetail.GetProperty("task").GetProperty("trigger").GetProperty("once").GetBoolean());
        Assert.False(cronDetail.GetProperty("task").GetProperty("trigger").GetProperty("once").GetBoolean());
    }

    [Fact]
    public async Task EditGoal_NextRunUsesItAndIsAudited()
    {
        TaskRecord task = await CreateAsync(_user, new NewTask { Goal = "Morning briefing.", Name = "briefing", Trigger = TaskTrigger.Cron("0 6 * * *") });

        HttpResponseMessage response = await _browser.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/goal", new { goal = "Morning briefing with the weather." }, Ct);
        HttpResponseMessage blank = await _browser.PutAsJsonAsync($"/api/v1/tasks/{task.Id}/goal", new { goal = " " }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Morning briefing with the weather.", (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("goal").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, blank.StatusCode);
        Assert.Equal("Morning briefing with the weather.", (await GetAsync(_user, task.Id)).Goal);
        AuditEvent audit = await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.SingleAsync(entry => entry.Action == "task.update_goal", Ct));
        Assert.Equal((task.Id.ToString(), _user.ToString()), (audit.Target, audit.ActorId));
    }

    [Fact]
    public async Task Cancel_CancelsTheTaskAndItsUnfinishedChildrenAndIsAudited()
    {
        TaskRecord parent = await CreateAsync(_user, new NewTask { Goal = "Plan the trip.", Trigger = TaskTrigger.At(DateTimeOffset.UtcNow.AddDays(1)) });
        TaskRecord child = await CreateAsync(_user, new NewTask { Goal = "Book the hotel.", ParentId = parent.Id, Trigger = TaskTrigger.At(DateTimeOffset.UtcNow.AddDays(1)) });

        HttpResponseMessage response = await _browser.PostAsync($"/api/v1/tasks/{parent.Id}/cancel", null, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(2, (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("cancelled").GetInt32());
        Assert.Equal((AgentTaskStatus.Cancelled, AgentTaskStatus.Cancelled), ((await GetAsync(_user, parent.Id)).Status, (await GetAsync(_user, child.Id)).Status));
        Assert.Equal(1, await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.CountAsync(entry => entry.Action == "task.cancel" && entry.Target == parent.Id.ToString(), Ct)));
    }

    [Fact]
    public async Task OtherUsersTask_NotFound()
    {
        (_, OwnerId other) = await Browser.SignInAsync(_factory, "user");
        TaskRecord foreign = await CreateAsync(other, new NewTask { Goal = "Not yours.", Trigger = TaskTrigger.At(DateTimeOffset.UtcNow.AddDays(1)) });

        HttpResponseMessage detail = await _browser.GetAsync($"/api/v1/tasks/{foreign.Id}", Ct);
        HttpResponseMessage goal = await _browser.PutAsJsonAsync($"/api/v1/tasks/{foreign.Id}/goal", new { goal = "Mine now." }, Ct);
        HttpResponseMessage cancel = await _browser.PostAsync($"/api/v1/tasks/{foreign.Id}/cancel", null, Ct);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound), (detail.StatusCode, goal.StatusCode, cancel.StatusCode));
        Assert.Equal(AgentTaskStatus.Pending, (await GetAsync(other, foreign.Id)).Status);
    }

    [Fact]
    public async Task KillSwitch_ActivateCancelsAgentTasks_ReleaseResumes()
    {
        TaskRecord task = await CreateAsync(_user, new NewTask { Goal = "Water the plants.", Trigger = TaskTrigger.At(DateTimeOffset.UtcNow.AddDays(1)) });

        JsonElement before = await _browser.GetFromJsonAsync<JsonElement>("/api/v1/tasks/kill-switch", Ct);
        HttpResponseMessage activated = await _browser.PostAsync("/api/v1/tasks/kill-switch/activate", null, Ct);
        JsonElement during = await _browser.GetFromJsonAsync<JsonElement>("/api/v1/tasks/kill-switch", Ct);
        HttpResponseMessage released = await _browser.PostAsync("/api/v1/tasks/kill-switch/release", null, Ct);
        JsonElement after = await _browser.GetFromJsonAsync<JsonElement>("/api/v1/tasks/kill-switch", Ct);

        Assert.False(before.GetProperty("active").GetBoolean());
        JsonElement activation = await activated.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal((true, 1), (activation.GetProperty("active").GetBoolean(), activation.GetProperty("cancelled").GetInt32()));
        Assert.True(during.GetProperty("active").GetBoolean());
        Assert.Equal(HttpStatusCode.OK, released.StatusCode);
        Assert.False(after.GetProperty("active").GetBoolean());
        Assert.Equal(AgentTaskStatus.Cancelled, (await GetAsync(_user, task.Id)).Status);
        Assert.Equal(1, await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.CountAsync(entry => entry.Action == "tasks.stop_all" && entry.ActorId == _user.ToString(), Ct)));
    }

    [Fact]
    public async Task Tasks_NeedAHumanSession()
    {
        HttpClient machine = await _factory.ClientWithTokenAsync(Marshal.Abstractions.Tokens.ApiScopes.Admin);

        HttpResponseMessage response = await machine.GetAsync("/api/v1/tasks", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    private Task<TaskRecord> CreateAsync(OwnerId owner, NewTask task) =>
        AsOwnerAsync(owner, async store => (await store.CreateAsync(task, Ct)).Value);

    private Task<TaskRecord> UpdateAsync(OwnerId owner, TaskRecord task) =>
        AsOwnerAsync(owner, async store => (await store.UpdateAsync(task, Ct)).Value);

    private Task<TaskRecord> GetAsync(OwnerId owner, TaskId id) =>
        AsOwnerAsync(owner, async store => (await store.GetAsync(id, Ct)).Value);

    private async Task<T> AsOwnerAsync<T>(OwnerId owner, Func<ITaskStore, Task<T>> work)
    {
        IServiceProvider services = _factory.Services;
        using (services.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Web }))
            return await work(services.GetRequiredService<ITaskStore>());
    }
}
