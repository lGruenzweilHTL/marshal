namespace Marshal.Abstractions.Events;

/// <summary>
/// Puts events on the event bus. Never blocks.
/// </summary>
public interface IEventPublisher
{
    void Publish(MarshalEvent @event);
}
