using System.Collections.Concurrent;
using System.Text.Json;
using Marshal.Abstractions.Events;
using Marshal.Tasks.Events;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Tasks.Tests.Events;

public sealed class ChannelEventBusTests : IAsyncLifetime
{
    private readonly RecordingMatcher _matcher = new();
    private readonly ConcurrentQueue<string> _seen = new();
    private ChannelEventBus _bus = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _bus = new ChannelEventBus(_matcher, NullLogger<ChannelEventBus>.Instance);
        await _bus.StartAsync(Ct);
    }

    public async ValueTask DisposeAsync()
    {
        await _bus.StopAsync(CancellationToken.None);
        _bus.Dispose();
    }

    [Fact]
    public async Task Publish_MatcherThenSubscribersInOrder()
    {
        _matcher.Seen = _seen;
        using IDisposable first = _bus.Subscribe((@event, _) => Record("first", @event));
        using IDisposable second = _bus.Subscribe((@event, _) => Record("second", @event));

        _bus.Publish(Event("a"));
        _bus.Publish(Event("b"));

        await WaitForAsync(() => _seen.Count == 6);
        Assert.Equal(["matcher:a", "first:a", "second:a", "matcher:b", "first:b", "second:b"], _seen);
    }

    [Fact]
    public async Task Publish_FailingHandlers_OthersStillReceive()
    {
        _matcher.Fail = true;
        using IDisposable failing = _bus.Subscribe((_, _) => throw new InvalidOperationException("boom"));
        using IDisposable working = _bus.Subscribe((@event, _) => Record("working", @event));

        _bus.Publish(Event("a"));
        _bus.Publish(Event("b"));

        await WaitForAsync(() => _seen.Count == 2);
        Assert.Equal(["working:a", "working:b"], _seen);
    }

    [Fact]
    public async Task Subscribe_Disposed_NoLongerReceives()
    {
        IDisposable gone = _bus.Subscribe((@event, _) => Record("gone", @event));
        using IDisposable staying = _bus.Subscribe((@event, _) => Record("staying", @event));
        gone.Dispose();

        _bus.Publish(Event("a"));

        await WaitForAsync(() => _seen.Count == 1);
        Assert.Equal(["staying:a"], _seen);
    }

    private ValueTask Record(string handler, MarshalEvent @event)
    {
        _seen.Enqueue($"{handler}:{@event.Entity}");
        return ValueTask.CompletedTask;
    }

    private static MarshalEvent Event(string entity) =>
        new("ha", "state_changed", entity, JsonDocument.Parse("{}").RootElement, null, DateTimeOffset.UnixEpoch);

    private static async Task WaitForAsync(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!condition())
            await Task.Delay(10, timeout.Token);
        await Task.Delay(50, Ct);
    }

    private sealed class RecordingMatcher : ITriggerMatcher
    {
        public ConcurrentQueue<string>? Seen { get; set; }

        public bool Fail { get; set; }

        public Task MatchAsync(MarshalEvent @event, CancellationToken cancellationToken)
        {
            if (Fail)
                throw new InvalidOperationException("matcher failed");
            Seen?.Enqueue($"matcher:{@event.Entity}");
            return Task.CompletedTask;
        }
    }
}
