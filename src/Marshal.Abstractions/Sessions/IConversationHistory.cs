using Microsoft.Extensions.AI;

namespace Marshal.Abstractions.Sessions;

/// <summary>
/// Earlier user turns, replies and background results of a session, rebuilt from its runs and handed-off tasks.
/// </summary>
public interface IConversationHistory
{
    /// <summary>
    /// The newest <paramref name="maxTurns"/> turns after <paramref name="after"/> (all when null) as user and assistant
    /// messages, oldest first. Background results are assistant messages. <c>CreatedAt</c> is the turn's start or the
    /// result's time.
    /// </summary>
    Task<IReadOnlyList<ChatMessage>> LoadAsync(SessionId sessionId, int maxTurns, DateTimeOffset? after, CancellationToken cancellationToken);
}
