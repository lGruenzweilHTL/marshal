using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Executors;

/// <summary>
/// Progress and completion posted by executors, authenticated with the session's <c>executor_callback</c> token only.
/// A token of another session gets 403; a token whose session ended (revoked) gets 401. Not part of the public API
/// description: only executors call it.
/// </summary>
internal static class ExecutorCallbackEndpoints
{
    private static readonly AuthorizationPolicy _callbackToken = new AuthorizationPolicyBuilder(MarshalAuthSchemes.ExecutorCallback)
        .RequireAuthenticatedUser()
        .RequireClaim(MarshalClaims.Task)
        .Build();

    public static RouteGroupBuilder MapExecutorCallbackEndpoints(this RouteGroupBuilder group)
    {
        group.MapPost("/executor/callbacks/{sessionId:guid}", HandleAsync)
            .RequireAuthorization(_callbackToken)
            .ExcludeFromDescription();
        return group;
    }

    private static async Task<Results<NoContent, ProblemHttpResult>> HandleAsync(
        Guid sessionId, ExecutorReport report, ClaimsPrincipal user, IExecutorCallbacks callbacks, CancellationToken ct)
    {
        if (!ApiTokenId.TryParse(user.FindFirstValue(MarshalClaims.TokenId), null, out ApiTokenId token))
            return new Error("executor.wrong_session", "This callback token belongs to another session.", ErrorType.Forbidden).ToProblem();

        Result handled = await callbacks.HandleAsync(sessionId, token, report, ct);
        return handled.IsSuccess ? TypedResults.NoContent() : handled.Error.ToProblem();
    }
}
