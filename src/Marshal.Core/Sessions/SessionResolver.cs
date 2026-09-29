using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Sessions;

/// <summary>
/// Finds or creates the session of a turn. Token clients are looked up by (token, client conversation id), never by
/// the client id alone; the owner is resolved once on the first turn and pinned. Idle sessions are closed,
/// their pending action dropped and the close listeners notified before a new session starts.
/// </summary>
/// <remarks>
/// The token lookup runs as the token's owner. In v1 the resolved owner is always the instance owner, which also
/// owns every token; multi-user resolution will revisit this.
/// </remarks>
public sealed class SessionResolver(
    ISessionStore sessions,
    IIdentityResolver identity,
    IPendingActionStore pendingActions,
    IEnumerable<ISessionCloseListener> closeListeners,
    IExecutionContextAccessor executionContext,
    IOptions<SessionOptions> options,
    TimeProvider timeProvider)
{
    public const string CookieResolvedBy = "cookie";

    public async Task<SessionRecord> ResolveTokenSessionAsync(
        ApiTokenId apiTokenId, OwnerId tokenOwner, string externalId, IdentityEvidence evidence, SessionSource source, CancellationToken cancellationToken)
    {
        using (executionContext.Use(SystemContext(tokenOwner, source)))
        {
            SessionRecord? existing = await sessions.FindOpenByTokenAsync(apiTokenId, externalId, cancellationToken);
            if (existing is not null && !IsIdle(existing))
            {
                await sessions.TouchAsync(existing.Id, cancellationToken);
                return existing;
            }

            if (existing is not null)
                await CloseAsync(existing, cancellationToken);
        }

        IdentityResolution resolution = await identity.ResolveAsync(evidence, cancellationToken);
        using (executionContext.Use(SystemContext(resolution.OwnerId, source)))
            return await sessions.CreateAsync(new NewSession(source, apiTokenId, externalId, evidence, resolution.ResolvedBy), cancellationToken);
    }

    public async Task<Result<SessionRecord>> ResolveOwnSessionAsync(SessionId? sessionId, OwnerId owner, SessionSource source, CancellationToken cancellationToken)
    {
        using IDisposable scope = executionContext.Use(SystemContext(owner, source));
        if (sessionId is null)
            return await sessions.CreateAsync(new NewSession(source, null, null, new IdentityEvidence(null, null, null, null), CookieResolvedBy), cancellationToken);

        SessionRecord? existing = await sessions.FindOpenAsync(sessionId.Value, cancellationToken);
        if (existing is not null && IsIdle(existing))
        {
            await CloseAsync(existing, cancellationToken);
            existing = null;
        }

        if (existing is null)
            return new Error("session.not_found", "The conversation does not exist or has ended.", ErrorType.NotFound);

        await sessions.TouchAsync(existing.Id, cancellationToken);
        return existing;
    }

    private bool IsIdle(SessionRecord session) => timeProvider.GetUtcNow() - session.LastTurnAt >= options.Value.IdleTtl;

    private async Task CloseAsync(SessionRecord session, CancellationToken cancellationToken)
    {
        await pendingActions.DropForSessionAsync(session.Id, cancellationToken);
        await sessions.CloseAsync(session.Id, cancellationToken);
        foreach (ISessionCloseListener listener in closeListeners)
            await listener.SessionClosedAsync(session.OwnerId, session.Id, cancellationToken);
    }

    private static MarshalExecutionContext SystemContext(OwnerId owner, SessionSource source) =>
        new() { OwnerId = owner, Source = source, IsInteractive = false };
}
