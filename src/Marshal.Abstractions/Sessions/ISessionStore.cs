using Marshal.Abstractions.Tokens;

namespace Marshal.Abstractions.Sessions;

/// <summary>
/// Sessions of the current owner.
/// </summary>
public interface ISessionStore
{
    /// <summary>The open session a token client created with this conversation id, or null.</summary>
    Task<SessionRecord?> FindOpenByTokenAsync(ApiTokenId apiTokenId, string externalId, CancellationToken cancellationToken);

    /// <summary>An open session by Marshal's own id, or null (also for other owners' sessions).</summary>
    Task<SessionRecord?> FindOpenAsync(SessionId id, CancellationToken cancellationToken);

    Task<SessionRecord> CreateAsync(NewSession session, CancellationToken cancellationToken);

    /// <summary>Sets the last turn time to now.</summary>
    Task TouchAsync(SessionId id, CancellationToken cancellationToken);

    /// <summary>Closes the session.</summary>
    Task CloseAsync(SessionId id, CancellationToken cancellationToken);

    /// <summary>Open sessions whose last turn is older than <paramref name="idleBefore"/>.</summary>
    Task<IReadOnlyList<SessionId>> FindIdleAsync(DateTimeOffset idleBefore, CancellationToken cancellationToken);
}
