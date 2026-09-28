namespace Marshal.Api.Conversation;

/// <summary>A reply text chunk (<c>delta</c> event).</summary>
public sealed record TurnDelta(string Text);

/// <summary>A progress message (<c>status</c> event).</summary>
public sealed record TurnStatus(string Text);

/// <summary>The last event of a turn (<c>final</c> event).</summary>
/// <param name="Speech">Full reply text.</param>
/// <param name="ContinueConversation">True when Marshal asked something and the client should keep listening.</param>
/// <param name="ConversationId">The client's conversation id for token clients, Marshal's session id otherwise.</param>
public sealed record TurnFinal(string Speech, bool ContinueConversation, string ConversationId);
