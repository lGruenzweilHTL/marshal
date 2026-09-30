using Marshal.Abstractions.Tokens;

namespace Marshal.Abstractions.Executors;

/// <summary>
/// Handles executor callbacks for the current owner (the owner of the presented callback token).
/// </summary>
public interface IExecutorCallbacks
{
    /// <summary>
    /// Applies a callback to session <paramref name="sessionId"/>. Fails with <c>executor.wrong_session</c>
    /// (forbidden) when <paramref name="token"/> is not that session's callback token.
    /// </summary>
    Task<Result> HandleAsync(Guid sessionId, ApiTokenId token, ExecutorReport report, CancellationToken cancellationToken);
}
