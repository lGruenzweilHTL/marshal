using Marshal.Abstractions.Sessions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace Marshal.Data.Conversations;

/// <summary>
/// Rebuilds the user / assistant exchange of a session from the context and reply steps of its runs, with background
/// results of its handed-off tasks as assistant messages.
/// </summary>
public sealed class EfConversationHistory(IDbContextFactory<MarshalDbContext> contexts) : IConversationHistory
{
    public async Task<IReadOnlyList<ChatMessage>> LoadAsync(SessionId sessionId, int maxTurns, DateTimeOffset? after, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<ConversationEntry> entries = await ConversationEntries.LoadAsync(db, sessionId, after, maxTurns, cancellationToken);

        var messages = new List<ChatMessage>();
        foreach (ConversationEntry entry in entries)
        {
            switch (entry)
            {
                case TurnEntry turn:
                    if (turn.Input is not null)
                        messages.Add(new ChatMessage(ChatRole.User, turn.Input) { CreatedAt = turn.At });
                    if (turn.Reply is not null)
                        messages.Add(new ChatMessage(ChatRole.Assistant, turn.Reply) { CreatedAt = turn.At });
                    break;
                case BackgroundResultEntry result:
                    messages.Add(new ChatMessage(ChatRole.Assistant, result.Body) { CreatedAt = result.At });
                    break;
            }
        }

        return messages;
    }
}
