using System.Collections.Concurrent;
using Marshal.Abstractions.LiveEvents;

namespace Marshal.Testing;

/// <summary>
/// Records published live events.
/// </summary>
public sealed class FakeLiveEventPublisher : ILiveEventPublisher
{
    public ConcurrentQueue<LiveEvent> Events { get; } = new();

    public void Publish(LiveEvent liveEvent) => Events.Enqueue(liveEvent);
}
