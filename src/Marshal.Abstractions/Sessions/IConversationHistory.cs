using Microsoft.Extensions.AI;

namespace Marshal.Abstractions.Sessions;

/// <summary>
/// Earlier user turns and replies of a session, rebuilt from its runs.
/// </summary>
public interface IConversationHistory
{
    Task<IReadOnlyList<ChatMessage>> LoadAsync(SessionId sessionId, int maxTurns, CancellationToken cancellationToken);
}
