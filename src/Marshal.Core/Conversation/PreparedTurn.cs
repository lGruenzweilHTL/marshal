using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Sessions;

namespace Marshal.Core.Conversation;

/// <summary>
/// A turn whose session, owner and language are known; ready to run.
/// </summary>
/// <param name="Turn">The request.</param>
/// <param name="Session">The session.</param>
/// <param name="Context">Execution context of the run.</param>
public sealed record PreparedTurn(ConversationTurn Turn, SessionRecord Session, MarshalExecutionContext Context)
{
    /// <summary><see cref="TimeProvider.GetTimestamp"/> when the turn arrived, for the time to first delta.</summary>
    public long? ReceivedAt { get; init; }
}
