using Marshal.Abstractions.Tasks;
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

    /// <summary>
    /// Checks that <paramref name="token"/> is the callback token of the running session <paramref name="sessionId"/>
    /// before the session uploads an artifact; returns the session's task.
    /// </summary>
    Task<Result<TaskId>> AuthorizeUploadAsync(Guid sessionId, ApiTokenId token, CancellationToken cancellationToken);
}
