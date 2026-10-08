using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Data.Identity;
using Marshal.Integrations.HomeAssistant;
using Marshal.Tasks.Deliveries;
using Marshal.Tasks.Events;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Limits;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Marshal.Tasks.Tests.Running;
using Marshal.Tasks.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Events;

/// <summary>
/// "Remind me to buy wall plugs when I'm at Hornbach": the model looks the zone up with <c>places_find</c>, creates a
/// once-watch on the owner's person entity, and Home Assistant state changes (as the HA event source publishes them)
/// fire it exactly once. Everything but the model and Home Assistant is real.
/// </summary>
public sealed class LocationReminderEndToEndTests : IAsyncLifetime
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(10);

    private readonly FakeUserPreferencesStore _preferences = new();
    private readonly ZoneCache _cache = new();
    private readonly TaskSignals _signals = new();
    private readonly RecordingDispatcher _dispatcher = new();
    private readonly CountingCommands _commands = new();
    private TaskLoopHarness _harness = null!;
    private TestDatabase _database = null!;
    private BackgroundTaskScheduler _scheduler = null!;
    private HomeAssistantEventSource _ha = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _preferences.Stored[OwnerA] = new UserPreferencesRecord("de", "Europe/Vienna", [], new NotificationTargets("person.alice", []), null);
        _harness = await TaskLoopHarness.CreateAsync(async harness =>
        {
            _database = await TestDatabase.CreateFileAsync(harness.Accessor);
            IReadOnlyList<ITool> tasks = await Tools(harness).GetToolsAsync(Ct);
            IReadOnlyList<ITool> places = await new PlacesToolSource(_cache, _commands, _preferences, Options.Create(new HomeAssistantOptions { Url = new Uri("http://ha.local/") })).GetToolsAsync(Ct);
            return [.. tasks, .. places];
        });
        var matcher = new DeclarativeTriggerMatcher(Store(), _signals, _harness.Accessor, _harness.Time, NullLogger<DeclarativeTriggerMatcher>.Instance);
        _ha = new HomeAssistantEventSource(new MatchingPublisher(matcher), _harness.Time);
        _scheduler = new BackgroundTaskScheduler(
            Store(), _signals, [_harness.Runner], _dispatcher, Budgets(), new TaskPauses(_database.Factory), _harness.Accessor, new ViennaProfiles(),
            Options.Create(new TaskOptions()), _harness.Time, NullLogger<BackgroundTaskScheduler>.Instance);
        await _scheduler.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _scheduler.StopAsync(CancellationToken.None);
        _scheduler.Dispose();
        _signals.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task FindThenOnceWatch_FiresOnTheFirstEntryOnly()
    {
        _harness.Model
            .ThenToolCall("places_find", new { query = "hornbach" })
            .ThenToolCall("tasks_watch", new
            {
                filter = new { source = "ha", entity = "person.alice", to = "Hornbach Linz" },
                goal = "Remind the user to buy wall plugs.",
                name = "wall-plugs",
                once = true,
            })
            .ThenText("Mach ich.");

        await _harness.Agent.RunAsync(new AgentRunRequest { Context = Text(OwnerA, "de"), UserText = "Erinnere mich an Dübel, wenn ich beim Hornbach bin." }, Ct);

        string found = _harness.Model.Requests[1].Messages.SelectMany(message => message.Contents).OfType<Microsoft.Extensions.AI.FunctionResultContent>().Single().Result!.ToString()!;
        Assert.Contains("\"personState\":\"Hornbach Linz\"", found, StringComparison.Ordinal);
        TaskRecord watch = await FindAsync("wall-plugs");
        Assert.Equal((TaskTriggerKind.Event, true, AgentTaskStatus.Pending), (watch.Trigger.Kind, watch.Trigger.Once, watch.Status));

        _harness.Model.ThenText("Denk an die Dübel!");
        await EnterAsync("not_home", "Hornbach Linz");
        await WaitForAsync(async () => (await FindAsync("wall-plugs")).Status == AgentTaskStatus.Done);
        await WaitIdleAsync();
        Assert.Equal([watch.Id], _dispatcher.Dispatched);

        await EnterAsync("Hornbach Linz", "not_home");
        await EnterAsync("not_home", "Hornbach Linz");
        await WaitIdleAsync();

        Assert.Equal([watch.Id], _dispatcher.Dispatched);
        Assert.Single(_harness.Recorder.Runs, run => run.Start.Context.Source == SessionSource.Task);
    }

    [Fact]
    public async Task CreateHere_VisibleWithAPersonAndFrozenForConfirmationWithoutACommand()
    {
        _harness.Model.ThenToolCall("places_create_here", new { name = "Baumarkt" }).ThenText("Bitte bestätigen.");

        await _harness.Agent.RunAsync(new AgentRunRequest { Context = Text(OwnerA, "de"), UserText = "Leg hier einen Ort Baumarkt an." }, Ct);

        Assert.Contains("places_find", _harness.Model.Requests[0].ToolNames);
        Assert.Contains("places_create_here", _harness.Model.Requests[0].ToolNames);
        Abstractions.Guard.PendingActionRecord pending = Assert.Single(_harness.PendingActions.Actions);
        Assert.Equal("places.create_here", pending.Tool.ToString());
        Assert.Equal(0, _commands.Sent);
    }

    [Fact]
    public async Task CreateHere_HiddenWithoutAPerson()
    {
        _preferences.Stored.Remove(OwnerA);
        _harness.Model.ThenText("Ok.");

        await _harness.Agent.RunAsync(new AgentRunRequest { Context = Text(OwnerA, "de"), UserText = "Wo bin ich?" }, Ct);

        Assert.Contains("places_find", _harness.Model.Requests[0].ToolNames);
        Assert.DoesNotContain("places_create_here", _harness.Model.Requests[0].ToolNames);
    }

    // The websocket message HA sends for a person's state change, as the cache hands it to the event source.
    private async Task EnterAsync(string from, string to)
    {
        var message = new JsonObject
        {
            ["event_type"] = "state_changed",
            ["data"] = new JsonObject
            {
                ["entity_id"] = "person.alice",
                ["old_state"] = new JsonObject { ["state"] = from, ["attributes"] = new JsonObject() },
                ["new_state"] = new JsonObject { ["state"] = to, ["attributes"] = new JsonObject { ["friendly_name"] = "Alice" } },
            },
        };
        _ha.Handle(JsonSerializer.SerializeToElement(message));
        await WaitIdleAsync();
    }

    private async Task<TaskRecord> FindAsync(string name)
    {
        using (_harness.Accessor.Use(Text(OwnerA)))
            return (await Store().FindAsync(name, Ct)).Value;
    }

    private async Task WaitIdleAsync()
    {
        await WaitForAsync(() => Task.FromResult(_scheduler.IsIdle));
        await Task.Delay(20, Ct);
        await WaitForAsync(() => Task.FromResult(_scheduler.IsIdle));
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow + _patience;
        while (!await condition())
        {
            if (DateTime.UtcNow > deadline)
                Assert.Fail("Condition not reached in time.");
            await Task.Delay(10, Ct);
        }
    }

    private EfTaskStore Store() => new(_database.Factory, _harness.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _harness.Time);

    private TaskBudgets Budgets() => new(Store(), new NoSpending(), new ViennaProfiles(), _harness.Templates, Options.Create(new TaskOptions()), _harness.Time);

    private TaskToolSource Tools(TaskLoopHarness harness) => new(
        Store(harness),
        _signals,
        new EfKillSwitch(_database.Factory, Store(harness), new EfUserPreferencesStore(_database.Factory, new FixedInstanceTimeZone()), _signals, new FakeAuditLog(), harness.Accessor, harness.Time),
        new TaskBudgets(Store(harness), new NoSpending(), new ViennaProfiles(), harness.Templates, Options.Create(new TaskOptions()), harness.Time),
        harness.ActiveRuns,
        new ViennaProfiles(),
        Options.Create(new TaskOptions()),
        harness.Time);

    private EfTaskStore Store(TaskLoopHarness harness) => new(_database.Factory, harness.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), harness.Time);

    private sealed class MatchingPublisher(DeclarativeTriggerMatcher matcher) : IEventPublisher
    {
        public void Publish(MarshalEvent @event) => matcher.MatchAsync(@event, CancellationToken.None).GetAwaiter().GetResult();
    }

    private sealed class RecordingDispatcher : IDeliveryDispatcher
    {
        public ConcurrentQueue<TaskId> Dispatched { get; } = new();

        public Task DispatchAsync(TaskId task, CancellationToken cancellationToken)
        {
            Dispatched.Enqueue(task);
            return Task.CompletedTask;
        }

        public Task ReviewPendingAsync(string? changedEntity, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class ZoneCache : IHomeAssistantCache
    {
        public HaSnapshot? Current { get; } = new()
        {
            Areas = ImmutableDictionary<string, HaArea>.Empty,
            Floors = ImmutableDictionary<string, HaFloor>.Empty,
            Devices = ImmutableDictionary<string, HaDevice>.Empty,
            Entities = ImmutableDictionary<string, HaEntity>.Empty,
            States = new[]
            {
                new HaState("zone.home", "0", JsonSerializer.SerializeToElement(new { radius = 100, passive = false, friendly_name = "Home" }), null),
                new HaState("zone.hornbach_linz", "0", JsonSerializer.SerializeToElement(new { radius = 200, passive = false, friendly_name = "Hornbach Linz" }), null),
                new HaState("person.alice", "not_home", JsonSerializer.SerializeToElement(new { friendly_name = "Alice" }), null),
            }.ToImmutableDictionary(state => state.EntityId),
        };

        public string? LastError => null;
    }

    private sealed class CountingCommands : IHomeAssistantCommands
    {
        public int Sent { get; private set; }

        public Task<Abstractions.Result<JsonElement>> CommandAsync(string type, JsonObject fields, CancellationToken cancellationToken)
        {
            Sent++;
            return Task.FromResult<Abstractions.Result<JsonElement>>(new Abstractions.Error("ha.command_failed", "not expected"));
        }
    }

    private sealed class NoSpending : ITaskSpending
    {
        public Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken) => Task.FromResult(0m);
    }
}
