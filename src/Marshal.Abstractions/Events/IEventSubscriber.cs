namespace Marshal.Abstractions.Events;

/// <summary>
/// Receives every event on the bus, in publish order, after the trigger matcher has seen it.
/// </summary>
public interface IEventSubscriber
{
    /// <summary>Calls <paramref name="handler"/> for each event from now on. Dispose to unsubscribe.</summary>
    IDisposable Subscribe(Func<MarshalEvent, CancellationToken, ValueTask> handler);
}
