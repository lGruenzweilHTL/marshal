using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Notifications;
using Marshal.Tasks.Maintenance;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Maintenance;

public sealed class MaintenanceTrackerTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeCounters _counters = new();
    private readonly FakeNotificationSender _sender = new();
    private readonly FakeUserPreferencesStore _preferences = new();
    private readonly CapturingSubscriber _events = new();
    private TestDatabase _database = null!;
    private MaintenanceTracker _tracker = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        var options = new MaintenanceOptions
        {
            Items =
            {
                ["voron_nozzle"] = new() { Name = "Voron nozzle", Source = new() { Printer = "voron", Metric = MaintenanceMetric.PrintHours }, Every = 300, WarnBefore = 20 },
                ["dishwasher_filter"] = new() { Name = "Dishwasher filter", Source = new() { Ha = "sensor.dishwasher_cycles" }, Every = 60 },
            },
        };
        _tracker = new MaintenanceTracker(
            Options.Create(options), [_counters], new Owner(), _preferences, _sender, _database.Factory, _database.Accessor, _events, _time, NullLogger<MaintenanceTracker>.Instance);
    }

    public async ValueTask DisposeAsync()
    {
        await _tracker.StopAsync(CancellationToken.None);
        _tracker.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task FirstSight_BaselineIsTheCurrentValue()
    {
        _counters.Values["voron"] = 1200;

        MaintenanceItemStatus status = (await _tracker.CheckAsync("voron_nozzle", Ct))!;

        Assert.Equal((0m, "ok", "h"), (status.Usage, status.State, status.Unit));
        Assert.Empty(_sender.Sent);
    }

    [Fact]
    public async Task Due_NotifiedOnce_SoonOnceBefore()
    {
        _counters.Values["voron"] = 1200;
        await _tracker.CheckAsync("voron_nozzle", Ct);

        _counters.Values["voron"] = 1485;
        await _tracker.CheckAsync("voron_nozzle", Ct);
        await _tracker.CheckAsync("voron_nozzle", Ct);
        _counters.Values["voron"] = 1500;
        MaintenanceItemStatus due = (await _tracker.CheckAsync("voron_nozzle", Ct))!;
        await _tracker.CheckAsync("voron_nozzle", Ct);

        Assert.Equal((300m, "due"), (due.Usage, due.State));
        Assert.Equal(["Maintenance due soon: Voron nozzle (285 / 300 h).", "Maintenance due: Voron nozzle (300 / 300 h)."], _sender.Sent.Select(sent => sent.Text));
        Assert.All(_sender.Sent, sent => Assert.Equal((NotificationPriority.Normal, OwnerA), (sent.Priority, sent.Owner)));
    }

    [Fact]
    public async Task JumpStraightToDue_OneNotification()
    {
        _counters.Values["voron"] = 1200;
        await _tracker.CheckAsync("voron_nozzle", Ct);
        _counters.Values["voron"] = 1600;

        await _tracker.CheckAsync("voron_nozzle", Ct);

        Assert.Equal("Maintenance due: Voron nozzle (400 / 300 h).", Assert.Single(_sender.Sent).Text);
    }

    [Fact]
    public async Task CounterReset_BaselineFollows_NoNotification()
    {
        _counters.Values["sensor.dishwasher_cycles"] = 30;
        await _tracker.CheckAsync("dishwasher_filter", Ct);
        _counters.Values["sensor.dishwasher_cycles"] = 80;
        await _tracker.CheckAsync("dishwasher_filter", Ct);
        _sender.Sent.Clear();

        _counters.Values["sensor.dishwasher_cycles"] = 2;
        MaintenanceItemStatus status = (await _tracker.CheckAsync("dishwasher_filter", Ct))!;

        Assert.Equal((0m, "ok"), (status.Usage, status.State));
        Assert.Empty(_sender.Sent);
        _counters.Values["sensor.dishwasher_cycles"] = 62;
        Assert.Equal("due", (await _tracker.CheckAsync("dishwasher_filter", Ct))!.State);
        Assert.Single(_sender.Sent);
    }

    [Fact]
    public async Task Done_ResetsUsage_AndNotificationsAgain()
    {
        _counters.Values["voron"] = 1200;
        await _tracker.CheckAsync("voron_nozzle", Ct);
        _counters.Values["voron"] = 1510;
        await _tracker.CheckAsync("voron_nozzle", Ct);

        MaintenanceItemStatus done = (await _tracker.DoneAsync("voron_nozzle", Ct)).Value;

        Assert.Equal((0m, "ok", _time.GetUtcNow()), (done.Usage, done.State, done.LastDoneAt));
        _counters.Values["voron"] = 1810;
        await _tracker.CheckAsync("voron_nozzle", Ct);
        Assert.Equal(2, _sender.Sent.Count(sent => sent.Text.StartsWith("Maintenance due:", StringComparison.Ordinal)));
        Assert.Equal("maintenance.unknown_item", (await _tracker.DoneAsync("toaster", Ct)).Error.Code);
    }

    [Fact]
    public async Task Events_ReadOnlyTheConcernedItems()
    {
        _counters.Values["voron"] = 1200;
        _counters.Values["sensor.dishwasher_cycles"] = 10;
        await _tracker.StartAsync(Ct);

        await _events.PublishAsync(new MarshalEvent("ha", "state_changed", "sensor.dishwasher_cycles", JsonSerializer.SerializeToElement(new { from = "9", to = "10" }), null, _time.GetUtcNow()));
        Assert.Equal(["sensor.dishwasher_cycles"], _counters.Reads);

        await _events.PublishAsync(new MarshalEvent("printer", "complete", "voron", JsonSerializer.SerializeToElement(new { }), null, _time.GetUtcNow()));
        await _events.PublishAsync(new MarshalEvent("ha", "state_changed", "light.kitchen", JsonSerializer.SerializeToElement(new { }), null, _time.GetUtcNow()));
        Assert.Equal(["sensor.dishwasher_cycles", "voron"], _counters.Reads);
    }

    [Fact]
    public async Task Hourly_ReadsEveryItem()
    {
        _counters.Values["voron"] = 1200;
        _counters.Values["sensor.dishwasher_cycles"] = 10;
        await _tracker.StartAsync(Ct);

        // The loop's delay may start after StartAsync returns (slow under load), and each next delay starts once the
        // reads are stored; advance a minute at a time until the hourly read has fired twice.
        await AdvanceUntilAsync(() => _counters.Reads.Count >= 2);
        Assert.Equal(["sensor.dishwasher_cycles", "voron"], _counters.Reads.Take(2).Order());
        await AdvanceUntilAsync(() => _counters.Reads.Count >= 4);

        Assert.False(_tracker.ExecuteTask!.IsFaulted);
    }

    [Fact]
    public async Task Status_DueFirst_UnreadableCounterKeepsLastValue()
    {
        _counters.Values["voron"] = 1200;
        _counters.Values["sensor.dishwasher_cycles"] = 10;
        await _tracker.CheckAllAsync(Ct);
        _counters.Values["sensor.dishwasher_cycles"] = 75;
        await _tracker.CheckAllAsync(Ct);
        _counters.Values.Remove("voron");

        IReadOnlyList<MaintenanceItemStatus> statuses = await _tracker.StatusAsync(Ct);

        Assert.Equal(["dishwasher_filter", "voron_nozzle"], statuses.Select(status => status.Id));
        Assert.Equal("due", statuses[0].State);
        Assert.Equal((0m, "printer offline"), (statuses[1].Usage, statuses[1].Error));
    }

    private async Task AdvanceUntilAsync(Func<bool> condition)
    {
        for (int attempt = 0; attempt < 600 && !condition(); attempt++)
        {
            _time.Advance(TimeSpan.FromMinutes(1));
            await Task.Delay(10, Ct);
        }

        Assert.True(condition());
    }

    private sealed class Owner : IInstanceOwner
    {
        public Task<OwnerId> GetAsync(CancellationToken cancellationToken) => Task.FromResult(OwnerA);
    }

    private sealed class FakeCounters : IMaintenanceCounterSource
    {
        public Dictionary<string, decimal> Values { get; } = [];

        public List<string> Reads { get; } = [];

        public bool Handles(MaintenanceSource source) => true;

        public Task<Result<MaintenanceReading>> ReadAsync(MaintenanceSource source, CancellationToken cancellationToken)
        {
            string key = source.Ha ?? source.Printer!;
            lock (Reads)
                Reads.Add(key);
            return Task.FromResult<Result<MaintenanceReading>>(Values.TryGetValue(key, out decimal value)
                ? new MaintenanceReading(value, source.Ha is null ? "h" : null)
                : new Error("printer.offline", "printer offline", ErrorType.Unavailable));
        }
    }

    private sealed class CapturingSubscriber : IEventSubscriber
    {
        private readonly List<Func<MarshalEvent, CancellationToken, ValueTask>> _handlers = [];

        public IDisposable Subscribe(Func<MarshalEvent, CancellationToken, ValueTask> handler)
        {
            _handlers.Add(handler);
            return new Unsubscribe(() => _handlers.Remove(handler));
        }

        public async Task PublishAsync(MarshalEvent @event)
        {
            foreach (Func<MarshalEvent, CancellationToken, ValueTask> handler in _handlers.ToList())
                await handler(@event, CancellationToken.None);
        }

        private sealed class Unsubscribe(Action action) : IDisposable
        {
            public void Dispose() => action();
        }
    }
}
