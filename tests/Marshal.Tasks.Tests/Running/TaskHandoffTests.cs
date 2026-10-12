using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Running;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Running;

public sealed class TaskHandoffTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly TaskContinuations _continuations = new();
    private readonly WakeCounter _scheduler = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task VoiceTurn_TaskForTheUserDueNow_SpokenOnTheOriginSatellite()
    {
        MarshalExecutionContext turn = Voice(OwnerA) with { RunId = RunId.New(), SessionId = SessionId.New() };
        turn.Taint.Mark("mail", 1);
        _database.Accessor.Current = turn;

        Result<TaskId> handed = await Handoff().HandOffAsync(new BackgroundHandoff(turn, "find the invoice", Transcript(), "latency_cap"), Ct);

        TaskRecord task = (await Store().GetAsync(handed.Value, Ct)).Value;
        Assert.Equal((OwnerA, TaskCreator.User, TaskKind.Agent, "find the invoice"), (task.OwnerId, task.CreatedBy, task.Kind, task.Goal));
        Assert.Equal((AgentTaskStatus.Pending, (DateTimeOffset?)_time.GetUtcNow(), "assist_satellite.kitchen"), (task.Status, task.NextRunAt, task.OriginSatelliteId));
        Assert.Equal(new TaskDelivery(DeliveryChannel.Voice, "assist_satellite.kitchen"), Assert.Single(task.Deliveries));
        Assert.Contains("latency_cap", task.Notes, StringComparison.Ordinal);
        Assert.Equal(turn.SessionId, task.OriginSessionId);
        Assert.Equal(1, _scheduler.Woken);
        Assert.True(_continuations.TryTake(task.Id, out Continuation? continuation));
        Assert.Equal(3, continuation.Transcript.Count);
        Assert.Equal(["mail"], continuation.Taint.Select(source => source.Kind));
    }

    [Fact]
    public async Task TextTurn_ResultToTheInbox()
    {
        MarshalExecutionContext turn = Text(OwnerA) with { SessionId = SessionId.New() };
        _database.Accessor.Current = turn;

        Result<TaskId> handed = await Handoff().HandOffAsync(new BackgroundHandoff(turn, "compare the offers", Transcript(), "move_to_background"), Ct);

        TaskRecord task = (await Store().GetAsync(handed.Value, Ct)).Value;
        Assert.Equal(new TaskDelivery(DeliveryChannel.Inbox), Assert.Single(task.Deliveries));
        Assert.Null(task.OriginSatelliteId);
        Assert.Equal(turn.SessionId, task.OriginSessionId);
    }

    [Fact]
    public async Task VoiceTurnWithoutSatellite_ResultPushed()
    {
        MarshalExecutionContext turn = Voice(OwnerA, satelliteId: null);
        _database.Accessor.Current = turn;

        Result<TaskId> handed = await Handoff().HandOffAsync(new BackgroundHandoff(turn, "find the invoice", Transcript(), "latency_cap"), Ct);

        Assert.Equal(DeliveryChannel.Push, Assert.Single((await Store().GetAsync(handed.Value, Ct)).Value.Deliveries).Channel);
    }

    [Fact]
    public async Task KillSwitchActive_Refused()
    {
        MarshalExecutionContext turn = Voice(OwnerA);
        _database.Accessor.Current = turn;
        await PauseAsync();

        Result<TaskId> handed = await Handoff().HandOffAsync(new BackgroundHandoff(turn, "find the invoice", Transcript(), "latency_cap"), Ct);

        Assert.Equal("tasks.paused", handed.Error.Code);
        Assert.Empty(await Store().ListAsync(new TaskFilter([AgentTaskStatus.Pending]), Ct));
    }

    private async Task PauseAsync()
    {
        var preferences = new Marshal.Data.Identity.EfUserPreferencesStore(_database.Factory, new FixedInstanceTimeZone());
        var killSwitch = new EfKillSwitch(_database.Factory, Store(), preferences, _scheduler, new FakeAuditLog(), _database.Accessor, _time);
        await killSwitch.ActivateAsync(Ct);
    }

    private static List<ChatMessage> Transcript() =>
    [
        new(ChatRole.User, "find the invoice"),
        new(ChatRole.Assistant, [new FunctionCallContent("call-1", "mail_search")]),
        new(ChatRole.Tool, [new FunctionResultContent("call-1", "ACME")]),
    ];

    private TaskHandoff Handoff() => new(Store(), new TaskPauses(_database.Factory), _continuations, _scheduler);

    private EfTaskStore Store() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private sealed class WakeCounter : ITaskScheduler
    {
        public int Woken { get; private set; }

        public void Wake() => Woken++;

        public void CancelRun(TaskId task)
        {
        }
    }
}
