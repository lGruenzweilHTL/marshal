using Marshal.Abstractions;
using Marshal.Abstractions.Sessions;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Conversation;

/// <summary>
/// The chat history (<c>/conversations</c>) for the signed-in user: web, voice and API conversations with their turns
/// and background results.
/// </summary>
internal static class ConversationHistoryEndpoints
{
    public static RouteGroupBuilder MapConversationHistoryEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/conversations", ListAsync).RequireAuthorization(Policies.Human).WithTags("Conversations").WithName("ListConversations")
            .WithSummary("The user's conversations (web, voice, api), newest last turn first; page with before (a lastTurnAt) and limit.");
        group.MapGet("/conversations/{id}", GetAsync).RequireAuthorization(Policies.Human).WithTags("Conversations").WithName("GetConversation")
            .WithSummary("One conversation: its turns and background results in time order.");
        return group;
    }

    private static async Task<Ok<IReadOnlyList<ConversationSummary>>> ListAsync(DateTimeOffset? before, int? limit, IConversationReader conversations, CancellationToken ct) =>
        TypedResults.Ok(await conversations.ListAsync(before, limit ?? 50, ct));

    private static async Task<Results<Ok<ConversationDetail>, ProblemHttpResult>> GetAsync(SessionId id, IConversationReader conversations, CancellationToken ct) =>
        await conversations.GetAsync(id, ct) is { } detail
            ? TypedResults.Ok(detail)
            : new Error("conversation.not_found", "The conversation does not exist.", ErrorType.NotFound).ToProblem();
}
