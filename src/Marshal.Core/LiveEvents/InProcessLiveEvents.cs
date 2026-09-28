using System.Collections.Concurrent;
using System.Threading.Channels;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.LiveEvents;

namespace Marshal.Core.LiveEvents;

/// <summary>
/// Live updates within this process: one bounded channel per subscriber, filtered by owner and topic when
/// published. A slow subscriber drops its oldest events; publishing never waits.
/// </summary>
public sealed class InProcessLiveEvents(int capacity = 256) : ILiveEventPublisher, ILiveEventSource
{
    private readonly ConcurrentDictionary<Subscription, byte> _subscriptions = new();

    public int SubscriberCount => _subscriptions.Count;

    public void Publish(LiveEvent liveEvent)
    {
        foreach (Subscription subscription in _subscriptions.Keys)
        {
            if (subscription.Owner == liveEvent.Owner && subscription.Topics.Contains(liveEvent.Topic))
                subscription.Channel.Writer.TryWrite(liveEvent);
        }
    }

    public ILiveSubscription Subscribe(OwnerId owner, IReadOnlySet<string> topics)
    {
        var channel = Channel.CreateBounded<LiveEvent>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.DropOldest, SingleReader = true });
        var subscription = new Subscription(owner, topics, channel, this);
        _subscriptions[subscription] = 0;
        return subscription;
    }

    private sealed class Subscription(OwnerId owner, IReadOnlySet<string> topics, Channel<LiveEvent> channel, InProcessLiveEvents events) : ILiveSubscription
    {
        public OwnerId Owner => owner;

        public IReadOnlySet<string> Topics => topics;

        public Channel<LiveEvent> Channel => channel;

        public async Task<LiveEvent?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken)
        {
            if (channel.Reader.TryRead(out LiveEvent? ready))
                return ready;

            using var timer = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timer.CancelAfter(timeout);
            try
            {
                return await channel.Reader.ReadAsync(timer.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                return null;
            }
        }

        public void Dispose()
        {
            events._subscriptions.TryRemove(this, out _);
            channel.Writer.TryComplete();
        }
    }
}
