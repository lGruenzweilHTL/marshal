namespace Marshal.Abstractions.Sessions;

/// <summary>
/// Tool namespaces a session loaded beyond the core namespaces, in load order, so later turns keep them.
/// </summary>
public interface ISessionToolNamespaces
{
    /// <summary>The session's loaded namespaces; empty for a new session or one that only used core namespaces.</summary>
    Task<IReadOnlyList<string>> GetAsync(SessionId sessionId, CancellationToken cancellationToken);

    /// <summary>Replaces the session's loaded namespaces.</summary>
    Task SaveAsync(SessionId sessionId, IReadOnlyList<string> namespaces, CancellationToken cancellationToken);
}
