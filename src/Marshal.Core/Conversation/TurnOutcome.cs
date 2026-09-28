namespace Marshal.Core.Conversation;

/// <summary>
/// The final event of a turn.
/// </summary>
/// <param name="Speech">Full reply text.</param>
/// <param name="ContinueConversation">True when Marshal asked something and keeps listening.</param>
/// <param name="ConversationId">The client's id for token clients, Marshal's session id otherwise.</param>
public sealed record TurnOutcome(string Speech, bool ContinueConversation, string ConversationId);
