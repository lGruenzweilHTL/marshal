using Marshal.Abstractions.Execution;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Conversation;

/// <summary>
/// Links a voice turn to the question it answers (its <c>followupRef</c>, or the reply window when the reference
/// did not arrive) and handles the question's target: a pending approval, a task waiting for the reply, or a
/// delivered result the user asks about.
/// </summary>
public interface IFollowUpReplies
{
    /// <summary>What the turn becomes, or null for a normal turn (no valid follow-up, or a reply that is not a decision).</summary>
    Task<FollowUpReply?> HandleAsync(ConversationTurn turn, MarshalExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>How a follow-up reply is answered.</summary>
/// <param name="FixedReply">The whole answer, without a model call (approval decided, task resumed); null for a model turn.</param>
/// <param name="Seed">Messages placed before the user's text (the delivered result the user asks about).</param>
public sealed record FollowUpReply(string? FixedReply, IReadOnlyList<ChatMessage> Seed);
