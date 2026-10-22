using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Api.Problems;
using Marshal.Demo;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

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
            .WithDescription("The current user: id, display name, roles and scopes.");
        group.MapGet("/auth/antiforgery", GetAntiforgeryToken)
            .AllowAnonymous()
            .WithTags("Auth")
            .WithName("GetAntiforgeryToken")
            .WithDescription("A token for the X-XSRF-TOKEN header of state-changing requests. Fetch a new one after login or logout.");
        return group;
    }

    private static async Task<Results<Ok<MeResponse>, ProblemHttpResult>> GetMeAsync(ClaimsPrincipal user, IUserProfileProvider profiles, IOptions<DemoOptions> demo, CancellationToken ct)
    {
        var owner = OwnerId.Parse(user.FindFirstValue(MarshalClaims.Owner)!, null);
        Result<UserProfile> profile = await profiles.GetAsync(owner, ct);
        if (profile.IsFailure)
            return profile.Error.ToProblem();

        List<string> roles = [.. user.FindAll(ClaimTypes.Role).Select(claim => claim.Value).Order(StringComparer.Ordinal)];
        List<string> scopes = [.. user.FindAll(MarshalClaims.Scope).Select(claim => claim.Value).Distinct().Order(StringComparer.Ordinal)];
        return TypedResults.Ok(new MeResponse(owner, profile.Value.DisplayName, roles, scopes, new DemoInfo(demo.Value.Enabled, demo.Value.ShowBadge)));
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
/// <param name="Demo">Whether the instance runs in demo mode and shows the badge.</param>
public sealed record MeResponse(OwnerId Id, string DisplayName, IReadOnlyList<string> Roles, IReadOnlyList<string> Scopes, DemoInfo Demo);

/// <summary>Demo mode as the UI needs it.</summary>
/// <param name="Enabled">Unconfigured integrations are simulated and demo data is seeded.</param>
/// <param name="ShowBadge">Show the "Demo" badge in the navigation.</param>
public sealed record DemoInfo(bool Enabled, bool ShowBadge);

/// <summary>Value for the <c>X-XSRF-TOKEN</c> header.</summary>
public sealed record AntiforgeryToken(string Token);
