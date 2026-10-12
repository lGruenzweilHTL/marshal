using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Sessions;

/// <summary>
/// Notified after a session went idle: a token session is closed, a web session rests (idle TTL). Used e.g. to
/// summarize the session into memory. Called on the closing or resting path, so implementations hand longer work to
/// a queue. A web session can rest several times.
/// </summary>
public interface ISessionCloseListener
{
    Task SessionClosedAsync(OwnerId owner, SessionId session, CancellationToken cancellationToken);
}
