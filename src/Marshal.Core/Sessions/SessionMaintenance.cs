using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Sessions;

/// <summary>
/// Closes idle sessions of one owner and drops their pending actions.
/// </summary>
public sealed class SessionMaintenance(
    ISessionStore sessions,
    IPendingActionStore pendingActions,
    IExecutionContextAccessor executionContext,
    IOptions<SessionOptions> options,
    TimeProvider timeProvider)
{
    public async Task<int> CloseIdleAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false });
        IReadOnlyList<SessionId> idle = await sessions.FindIdleAsync(timeProvider.GetUtcNow() - options.Value.IdleTtl, cancellationToken);
        foreach (SessionId session in idle)
        {
            await pendingActions.DropForSessionAsync(session, cancellationToken);
            await sessions.CloseAsync(session, cancellationToken);
        }

        return idle.Count;
    }
}
