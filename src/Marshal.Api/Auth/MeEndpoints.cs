using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Auth;

/// <summary>
/// The current principal (<c>/me</c>) and the SPA's antiforgery token.
/// </summary>
internal static class MeEndpoints
{
    public static RouteGroupBuilder MapMeEndpoints(this RouteGroupBuilder group)
    {
        group.MapGet("/me", GetMeAsync)
            .RequireAuthorization()
            .WithTags("Me")
            .WithName("GetMe")
            .WithSummary("The current user: id, display name, roles and scopes.");
        group.MapGet("/auth/antiforgery", GetAntiforgeryToken)
            .AllowAnonymous()
            .WithTags("Auth")
            .WithName("GetAntiforgeryToken")
            .WithSummary("A token for the X-XSRF-TOKEN header of state-changing requests. Fetch a new one after login or logout.");
        return group;
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> GetMeAsync(ClaimsPrincipal user, IUserProfileProvider profiles, CancellationToken ct)
    {
        var owner = OwnerId.Parse(user.FindFirstValue(MarshalClaims.Owner)!, null);
        Result<UserProfile> profile = await profiles.GetAsync(owner, ct);
        if (profile.IsFailure)
            return profile.Error.ToProblem();

        List<string> roles = [.. user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Order(StringComparer.Ordinal)];
        List<string> scopes = [.. user.FindAll(MarshalClaims.Scope).Select(claim => claim.Value).Distinct().Order(StringComparer.Ordinal)];
        return TypedResults.Ok(new MeResponse(owner, profile.Value.DisplayName, roles, scopes));
    }

    private static Results<Ok<AntiforgeryToken>, ProblemHttpResult> GetAntiforgeryToken(HttpContext context, IAntiforgery antiforgery)
    {
        context.Response.Headers.CacheControl = "no-store";
        // The antiforgery cookie is Secure-only; over plain HTTP GetAndStoreTokens would throw.
        if (!context.Request.IsHttps)
            return AntiforgeryErrors.HttpsRequired.ToProblem();

        return TypedResults.Ok(new AntiforgeryToken(antiforgery.GetAndStoreTokens(context).RequestToken!));
    }
}

/// <summary>The current user.</summary>
/// <param name="Id">Owner id of the user.</param>
/// <param name="DisplayName">Name used in the UI and the system prompt.</param>
/// <param name="Roles">Roles (empty for token principals).</param>
/// <param name="Scopes">Scopes: granted by the roles for cookie sessions, the token's own for tokens.</param>
public sealed record MeResponse(OwnerId Id, string DisplayName, IReadOnlyList<string> Roles, IReadOnlyList<string> Scopes);

/// <summary>Value for the <c>X-XSRF-TOKEN</c> header.</summary>
public sealed record AntiforgeryToken(string Token);
