using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks;

/// <summary>
/// Live-event topic of conversations: background results that join the session they were handed off from.
/// </summary>
public static class ConversationLiveEvents
{
    /// <summary>A handed-off task produced its result (<c>conversation.result</c>).</summary>
    public const string Conversations = "conversations";

    public const string Result = "conversation.result";
}

/// <summary>Payload of <c>conversation.result</c>.</summary>
public sealed record ConversationResultPayload(SessionId SessionId, TaskId TaskId, DateTimeOffset ResultAt, string Body);
