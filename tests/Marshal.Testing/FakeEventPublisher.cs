using System.Collections.Concurrent;
using Marshal.Abstractions.Events;

namespace Marshal.Testing;

/// <summary>
/// Records published events.
/// </summary>
public sealed class FakeEventPublisher : IEventPublisher
{
    public ConcurrentQueue<MarshalEvent> Events { get; } = new();

    public void Publish(MarshalEvent @event) => Events.Enqueue(@event);
}
