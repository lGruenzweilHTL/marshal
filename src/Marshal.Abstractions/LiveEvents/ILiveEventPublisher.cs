namespace Marshal.Abstractions.LiveEvents;

/// <summary>
/// Publishes live updates. Never blocks; slow subscribers lose their oldest events.
/// </summary>
public interface ILiveEventPublisher
{
    void Publish(LiveEvent liveEvent);
}
