using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Sessions;

/// <summary>
/// Notified after a session is closed (idle TTL), e.g. to summarize it into memory. Called on the closing path, so
/// implementations hand longer work to a queue.
/// </summary>
public interface ISessionCloseListener
{
    Task SessionClosedAsync(OwnerId owner, SessionId session, CancellationToken cancellationToken);
}
