using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Printers;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class PrintBabysitterTests : IAsyncDisposable
{
    private static readonly TimeSpan _interval = TimeSpan.FromSeconds(30);

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));
    private readonly CapturingSubscriber _events = new();
    private readonly FakePrinterStateCache _states = new();
    private readonly FakeSnapshotSource _snapshots = new() { Links = new SignedLinks() };
    private readonly ScriptedDetector _detector = new();
    private readonly FakePrinterClient _client = new();
    private readonly FakeNotificationSender _notifications = new();
    private readonly FakeExecutionContextAccessor _context = new();
    private readonly PrintBabysitter _babysitter;

    public PrintBabysitterTests() => _babysitter = new PrintBabysitter(
        Options.Create(new PrinterOptions
        {
            Printers =
            [
                new PrinterConfig { Name = "voron", Babysitter = new BabysitterOptions { Enabled = true, Interval = _interval } },
                new PrinterConfig { Name = "prusa" },
            ],
        }),
        _events,
        _states,
        _snapshots,
        new SignedLinks(),
        _detector,
        _client,
        _notifications,
        new FixedOwner(),
        new GermanProfiles(),
        _context,
        _time,
        NullLogger<PrintBabysitter>.Instance);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        await _babysitter.StopAsync(CancellationToken.None);
        _babysitter.Dispose();
    }

    [Fact]
    public async Task StartedEvent_WatchesOnItsInterval_StopsOnComplete()
    {
        await _babysitter.StartAsync(Ct);
        Assert.False(_babysitter.Get("voron")!.Active);

        await _events.PublishAsync(Event("started"));
        Assert.True(_babysitter.Get("voron")!.Active);
        _time.Advance(_interval);
        await WaitUntilAsync(() => _detector.Checked == 1);
        _time.Advance(_interval);
        await WaitUntilAsync(() => _detector.Checked == 2);

        await _events.PublishAsync(Event("complete"));
        Assert.False(_babysitter.Get("voron")!.Active);
        _time.Advance(_interval * 3);
        await Task.Delay(50, Ct);
        Assert.Equal(2, _detector.Checked);
    }

    [Theory]
    [InlineData("cancelled")]
    [InlineData("error")]
    [InlineData("paused")]
    [InlineData("klippy_shutdown")]
    public async Task EndingEvents_StopWatching(string type)
    {
        await _babysitter.StartAsync(Ct);
        await _events.PublishAsync(Event("started"));

        await _events.PublishAsync(Event(type));

        Assert.False(_babysitter.Get("voron")!.Active);
    }

    [Fact]
    public async Task NotEnabledPrinter_NoStatusAndNoWatch()
    {
        await _babysitter.StartAsync(Ct);

        await _events.PublishAsync(Event("started", "prusa"));

        Assert.Null(_babysitter.Get("prusa"));
        Assert.Null(_babysitter.Get("unknown"));
    }

    [Fact]
    public async Task AlreadyPrintingAtStartup_Watched()
    {
        _states.Set("voron", Printing());

        await _babysitter.StartAsync(Ct);

        Assert.True(_babysitter.Get("voron")!.Active);
    }

    [Fact]
    public async Task OnePositiveThenNegative_NoPause()
    {
        await StartWatchingAsync();
        _detector.Verdicts.Enqueue(new PrintVerdict(true, "spaghetti"));
        _detector.Verdicts.Enqueue(new PrintVerdict(false, "looks fine"));
        _detector.Verdicts.Enqueue(new PrintVerdict(true, "spaghetti"));

        for (int check = 0; check < 3; check++)
            await _babysitter.CheckAsync("voron", Ct);

        Assert.Empty(_client.Sent);
        Assert.Empty(_notifications.Sent);
        Assert.Equal(["failure", "ok", "failure"], _babysitter.Get("voron")!.Verdicts.Select(check => check.Outcome));
    }

    [Fact]
    public async Task UnjudgedFrameBetweenPositives_NoPause()
    {
        await StartWatchingAsync();
        _detector.Verdicts.Enqueue(new PrintVerdict(true, "spaghetti"));
        _detector.Verdicts.Enqueue(null);
        _detector.Verdicts.Enqueue(new PrintVerdict(true, "spaghetti"));

        for (int check = 0; check < 3; check++)
            await _babysitter.CheckAsync("voron", Ct);

        Assert.Empty(_client.Sent);
        Assert.Equal("unknown", _babysitter.Get("voron")!.Verdicts[1].Outcome);
    }

    [Fact]
    public async Task TwoConsecutivePositives_PausesDirectly_CriticalNotificationWithSnapshot()
    {
        await StartWatchingAsync();
        _detector.Verdicts.Enqueue(new PrintVerdict(true, "spaghetti"));
        _detector.Verdicts.Enqueue(new PrintVerdict(true, "spaghetti around the nozzle"));

        await _babysitter.CheckAsync("voron", Ct);
        await _babysitter.CheckAsync("voron", Ct);

        Assert.Equal(("voron", (PrinterCommand)new PrinterCommand.Pause()), Assert.Single(_client.Sent));
        NotificationRequest sent = Assert.Single(_notifications.Sent);
        Assert.Equal((OwnerA, "system:babysitter", NotificationPriority.Critical), (sent.Owner, sent.Source, sent.Priority));
        Assert.True(sent.IsSystem);
        Assert.Equal("Der Druck auf voron sieht fehlgeschlagen aus (spaghetti around the nozzle). Ich habe ihn pausiert.", sent.Text);
        Assert.Equal(("printers/voron", "/signed/voron/snap1", "babysitter-voron"), (sent.Link, sent.Image, sent.Tag));

        BabysitterStatus status = _babysitter.Get("voron")!;
        Assert.Equal((false, (DateTimeOffset?)_time.GetUtcNow()), (status.Active, status.PausedAt));
    }

    [Fact]
    public async Task PauseFails_StillCriticalNotificationSayingSo()
    {
        await StartWatchingAsync();
        _client.Failure = new Error("printer.offline", "The printer is offline.", ErrorType.Unavailable);
        _detector.Verdicts.Enqueue(new PrintVerdict(true, "spaghetti"));
        _detector.Verdicts.Enqueue(new PrintVerdict(true, "spaghetti"));

        await _babysitter.CheckAsync("voron", Ct);
        await _babysitter.CheckAsync("voron", Ct);

        Assert.Contains("Pausieren ist fehlgeschlagen", Assert.Single(_notifications.Sent).Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Verdicts_KeepTheLastFive()
    {
        await StartWatchingAsync();
        for (int check = 0; check < 7; check++)
        {
            _detector.Verdicts.Enqueue(new PrintVerdict(false, $"fine {check}"));
            await _babysitter.CheckAsync("voron", Ct);
        }

        Assert.Equal(["fine 2", "fine 3", "fine 4", "fine 5", "fine 6"], _babysitter.Get("voron")!.Verdicts.Select(check => check.Reason));
        Assert.Equal("/api/printers/voron/snapshots/snap1", _babysitter.Get("voron")!.Verdicts[0].SnapshotUrl);
    }

    private async Task StartWatchingAsync()
    {
        await _babysitter.StartAsync(Ct);
        await _events.PublishAsync(Event("started"));
    }

    private MarshalEvent Event(string type, string printer = "voron") =>
        new("printer", type, printer, JsonSerializer.SerializeToElement(new { }), null, _time.GetUtcNow());

    private static PrinterState Printing() => new(
        "voron", "ready", null, "printing", "benchy.gcode", 0.25, 600, 720, 812.5, 30, 120, 1800, null,
        new HeaterState(200, 245, 1), new HeaterState(100, 100, 0.5), DateTimeOffset.UnixEpoch);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var waited = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && waited.Elapsed < TimeSpan.FromSeconds(5))
            await Task.Delay(10, Ct);
        Assert.True(condition());
    }

    private sealed class ScriptedDetector : IPrintFailureDetector
    {
        private int _checked;

        /// <summary>Verdicts in order; null = no verdict. Empty = not a failure.</summary>
        public Queue<PrintVerdict?> Verdicts { get; } = new();

        public int Checked => Volatile.Read(ref _checked);

        public Task<Result<PrintVerdict>> DetectAsync(PrinterSnapshot snapshot, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _checked);
            PrintVerdict? verdict = Verdicts.TryDequeue(out PrintVerdict? next) ? next : new PrintVerdict(false, "looks fine");
            return Task.FromResult(verdict is null
                ? Result.Failure<PrintVerdict>(new Error("printer.verdict_invalid", "No verdict.", ErrorType.Failure))
                : Result.Success(verdict));
        }
    }

    private sealed class SignedLinks : ISnapshotLinks
    {
        public string For(string printer, string id) => $"/api/printers/{printer}/snapshots/{id}";

        public string Shared(string printer, string id) => $"/signed/{printer}/{id}";
    }

    private sealed class FixedOwner : IInstanceOwner
    {
        public Task<OwnerId> GetAsync(CancellationToken cancellationToken) => Task.FromResult(OwnerA);
    }

    private sealed class GermanProfiles : IUserProfileProvider
    {
        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new UserProfile(owner, "Lukas", "de", "Europe/Vienna")));
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
