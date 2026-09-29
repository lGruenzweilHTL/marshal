using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Deliveries;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Deliveries;

public sealed class DeliveryDispatcherTests : IAsyncLifetime
{
    private const string Kitchen = "state(binary_sensor.kitchen_occupancy = on)";

    // 2026-09-29 04:05 UTC = 06:05 in Vienna.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 4, 5, 0, TimeSpan.Zero));
    private readonly FakeNotificationSender _sender = new();
    private readonly FakeConditions _conditions = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Dispatch_UnconditionalDeliveries_EachSentIndependently()
    {
        TaskRecord task = await CreateWithResultAsync(
            new TaskDelivery(DeliveryChannel.Voice, "assist_satellite.kitchen", FollowUp: true),
            new TaskDelivery(DeliveryChannel.Inbox, Priority: NotificationPriority.Low),
            new TaskDelivery(DeliveryChannel.Silent));

        await Dispatcher().DispatchAsync(task.Id, Ct);

        NotificationRequest[] sent = [.. _sender.Sent];
        Assert.Equal(2, sent.Length);
        Assert.Equal((OwnerA, $"task:{task.Id}", "Good morning.", DeliveryChannel.Voice, "assist_satellite.kitchen", true, NotificationPriority.Normal), (sent[0].Owner, sent[0].Source, sent[0].Text, sent[0].Channel, sent[0].Target, sent[0].FollowUp, sent[0].Priority));
        Assert.Equal((DeliveryChannel.Inbox, NotificationPriority.Low, (TaskId?)task.Id, $"tasks/{task.Id}"), (sent[1].Channel, sent[1].Priority, sent[1].TaskId, sent[1].Link));
    }

    [Fact]
    public async Task Dispatch_ConditionsHold_SentImmediately()
    {
        _conditions.States["binary_sensor.kitchen_occupancy"] = "on";
        TaskRecord task = await CreateWithResultAsync(new TaskDelivery(DeliveryChannel.Voice, When: [Kitchen]));

        await Dispatcher().DispatchAsync(task.Id, Ct);

        Assert.Single(_sender.Sent);
        Assert.Null(Assert.Single((await GetAsync(task.Id)).Deliveries).PendingSince);
    }

    [Fact]
    public async Task StateChange_PendingDeliverySentWhenConditionsHold()
    {
        _conditions.States["binary_sensor.kitchen_occupancy"] = "off";
        TaskRecord task = await CreateWithResultAsync(new TaskDelivery(DeliveryChannel.Voice, When: [Kitchen]));
        await Dispatcher().DispatchAsync(task.Id, Ct);
        Assert.Empty(_sender.Sent);
        Assert.Equal(_time.GetUtcNow(), Assert.Single((await GetAsync(task.Id)).Deliveries).PendingSince);

        _time.Advance(TimeSpan.FromMinutes(65)); // 07:10
        _conditions.States["binary_sensor.kitchen_occupancy"] = "on";
        _database.Accessor.Current = null;
        await Dispatcher().ReviewPendingAsync("binary_sensor.kitchen_occupancy", Ct);

        Assert.Equal(DeliveryChannel.Voice, Assert.Single(_sender.Sent).Channel);
        _database.Accessor.Current = Text(OwnerA);
        Assert.Null(Assert.Single((await GetAsync(task.Id)).Deliveries).PendingSince);
        _database.Accessor.Current = null;
        await Dispatcher().ReviewPendingAsync("binary_sensor.kitchen_occupancy", Ct);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Review_AllConditionsRequired()
    {
        _conditions.States["binary_sensor.kitchen_occupancy"] = "on";
        _conditions.States["zone.home"] = "0";
        TaskRecord task = await CreateWithResultAsync(new TaskDelivery(DeliveryChannel.Voice, When: [Kitchen, "state(zone.home > 0)"]));
        await Dispatcher().DispatchAsync(task.Id, Ct);
        _database.Accessor.Current = null;

        await Dispatcher().ReviewPendingAsync("binary_sensor.kitchen_occupancy", Ct);
        Assert.Empty(_sender.Sent);

        _conditions.States["zone.home"] = "1";
        await Dispatcher().ReviewPendingAsync("zone.home", Ct);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Review_ExpiryInOwnersTimeZone_SentOnFallback()
    {
        _conditions.States["binary_sensor.kitchen_occupancy"] = "off";
        TaskRecord task = await CreateWithResultAsync(new TaskDelivery(DeliveryChannel.Voice, When: [Kitchen], Expires: "11:00", Fallback: DeliveryChannel.Push));
        await Dispatcher().DispatchAsync(task.Id, Ct);
        _database.Accessor.Current = null;

        _time.SetUtcNow(new DateTimeOffset(2026, 9, 29, 8, 59, 0, TimeSpan.Zero)); // 10:59 in Vienna
        await Dispatcher().ReviewPendingAsync(null, Ct);
        Assert.Empty(_sender.Sent);

        _time.SetUtcNow(new DateTimeOffset(2026, 9, 29, 9, 0, 0, TimeSpan.Zero)); // 11:00 in Vienna
        await Dispatcher().ReviewPendingAsync(null, Ct);

        NotificationRequest sent = Assert.Single(_sender.Sent);
        Assert.Equal((DeliveryChannel.Push, true, false), (sent.Channel, sent.IsFallback, sent.FollowUp));
        _database.Accessor.Current = Text(OwnerA);
        Assert.Null(Assert.Single((await GetAsync(task.Id)).Deliveries).PendingSince);
    }

    [Fact]
    public async Task Dispatch_NewResult_ReplacesWaitingOne()
    {
        _conditions.States["binary_sensor.kitchen_occupancy"] = "off";
        TaskRecord task = await CreateWithResultAsync(new TaskDelivery(DeliveryChannel.Voice, When: [Kitchen]));
        await Dispatcher().DispatchAsync(task.Id, Ct);
        TaskRecord current = await GetAsync(task.Id);
        await new EfTaskStore(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time)
            .UpdateAsync(current with { Result = new TaskResult("Second.", "Second.") }, Ct);
        _time.Advance(TimeSpan.FromMinutes(1));
        await Dispatcher().DispatchAsync(task.Id, Ct);

        _conditions.States["binary_sensor.kitchen_occupancy"] = "on";
        _database.Accessor.Current = null;
        await Dispatcher().ReviewPendingAsync("binary_sensor.kitchen_occupancy", Ct);

        Assert.Equal("Second.", Assert.Single(_sender.Sent).Text);
    }

    private DeliveryDispatcher Dispatcher() => new(Store(), _conditions, _sender, _database.Accessor, new ViennaProfiles(), _time, NullLogger<DeliveryDispatcher>.Instance);

    private EfTaskStore Store() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private async Task<TaskRecord> CreateWithResultAsync(params TaskDelivery[] deliveries)
    {
        TaskRecord task = (await Store().CreateAsync(new NewTask { Goal = "Briefing.", Trigger = TaskTrigger.Now(), Deliveries = deliveries }, Ct)).Value;
        return (await Store().UpdateAsync(task with { Status = AgentTaskStatus.Done, Result = new TaskResult("Good morning.", "Good morning.\n\nDetails."), ResultAt = _time.GetUtcNow() }, Ct)).Value;
    }

    private async Task<TaskRecord> GetAsync(TaskId id) => (await Store().GetAsync(id, Ct)).Value;

    private sealed class FakeConditions : IConditionEvaluator
    {
        public Dictionary<string, string> States { get; } = [];

        public Task<bool> AllHoldAsync(IReadOnlyList<StateCondition> conditions, OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult(conditions.All(condition => condition.HoldsFor(States.GetValueOrDefault(condition.Entity))));
    }
}
