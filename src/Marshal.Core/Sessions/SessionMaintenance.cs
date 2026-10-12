using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Sessions;

/// <summary>
/// Ends idle sessions of one owner: token sessions (voice, API) and task sessions are closed, web sessions rest and
/// stay continuable. Both drop pending actions and notify the close listeners.
/// </summary>
public sealed class SessionMaintenance(
    ISessionStore sessions,
    IPendingActionStore pendingActions,
    IEnumerable<ISessionCloseListener> closeListeners,
    IExecutionContextAccessor executionContext,
    IOptions<SessionOptions> options,
    TimeProvider timeProvider)
{
    public async Task<int> CloseIdleAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false });
        IReadOnlyList<SessionRecord> idle = await sessions.FindIdleAsync(timeProvider.GetUtcNow() - options.Value.IdleTtl, cancellationToken);
        foreach (SessionRecord session in idle)
            await EndIdleAsync(sessions, pendingActions, closeListeners, session, cancellationToken);

        return idle.Count;
    }

    /// <summary>Closes an idle session, or rests it when it is a web session.</summary>
    internal static async Task EndIdleAsync(
        ISessionStore sessions, IPendingActionStore pendingActions, IEnumerable<ISessionCloseListener> closeListeners, SessionRecord session, CancellationToken cancellationToken)
    {
        await pendingActions.DropForSessionAsync(session.Id, cancellationToken);
        if (session.Source == SessionSource.Web)
            await sessions.RestAsync(session.Id, cancellationToken);
        else
            await sessions.CloseAsync(session.Id, cancellationToken);

        foreach (ISessionCloseListener listener in closeListeners)
            await listener.SessionClosedAsync(session.OwnerId, session.Id, cancellationToken);
    }
}
