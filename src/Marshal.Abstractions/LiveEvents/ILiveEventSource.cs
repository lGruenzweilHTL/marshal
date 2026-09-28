using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.LiveEvents;

/// <summary>
/// Subscribes to live updates of one owner.
/// </summary>
public interface ILiveEventSource
{
    /// <summary>Starts receiving immediately; events are buffered until read. Dispose to unsubscribe.</summary>
    ILiveSubscription Subscribe(OwnerId owner, IReadOnlySet<string> topics);
}

/// <summary>
/// An active subscription.
/// </summary>
public interface ILiveSubscription : IDisposable
{
    /// <summary>Waits for the next event; false when <paramref name="timeout"/> passed without one.</summary>
    Task<LiveEvent?> ReadAsync(TimeSpan timeout, CancellationToken cancellationToken);
}
