using Marshal.Abstractions;
using Marshal.Api.Problems;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Auth;

/// <summary>
/// Login provider list, login start and logout on the versioned API group. Protocol callbacks
/// (<c>/signin-oidc/{name}</c>, <c>/signout-callback-oidc/{name}</c>) are handled by the OIDC handlers, unversioned.
/// </summary>
internal static class AuthEndpoints
{
    public static RouteGroupBuilder MapAuthEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder auth = group.MapGroup("/auth").WithTags("Auth");
        auth.MapGet("/providers", ListProviders)
            .AllowAnonymous()
            .WithName("ListLoginProviders")
            .WithDescription("Lists the active login providers.");
        auth.MapGet("/login/{provider}", StartLogin)
            .AllowAnonymous()
            .WithName("StartLogin")
            .WithDescription("Starts a login with the provider and redirects back to returnUrl (an app-relative path) afterwards.");
        auth.MapPost("/logout", Logout)
            .RequireAuthorization()
            .WithName("Logout")
            .WithDescription("Ends the session (and the IdP session for OIDC logins) and redirects to the login page.");
        return group;
    }

    private static Ok<List<LoginProviderInfo>> ListProviders(IReadOnlyList<ILoginProvider> providers) =>
        TypedResults.Ok(providers.Select(provider => new LoginProviderInfo(provider.Name, provider.Type, provider.DisplayName)).ToList());

    private static IResult StartLogin(string provider, string? returnUrl, IReadOnlyList<ILoginProvider> providers, HttpContext context)
    {
        ILoginProvider? match = providers.FirstOrDefault(candidate => candidate.Name == provider);
        if (match is null)
            return new Error("auth.provider_not_found", $"Login provider '{provider}' is not configured.", ErrorType.NotFound).ToProblem();

        returnUrl ??= ReturnUrls.Default;
        return ReturnUrls.IsLocal(returnUrl)
            ? match.Start(context, returnUrl)
            : new Error("auth.invalid_return_url", "returnUrl must be an app-relative path.", ErrorType.Validation).ToProblem();
    }

    private static SignOutHttpResult Logout(HttpContext context, IReadOnlyList<ILoginProvider> providers)
    {
        var properties = new AuthenticationProperties { RedirectUri = context.Request.PathBase + "/login" };
        string? loginProvider = context.User.FindFirst(MarshalClaims.LoginProvider)?.Value;
        bool oidc = providers.Any(provider => provider.Name == loginProvider && provider.Type == LoginProviderOptions.OidcType);
        return oidc
            ? TypedResults.SignOut(properties, [MarshalAuthSchemes.Cookie, loginProvider!])
            : TypedResults.SignOut(properties, [MarshalAuthSchemes.Cookie]);
    }
}

/// <summary>A login provider as shown on the login page.</summary>
/// <param name="Name">Id used in <c>/api/v1/auth/login/{name}</c>.</param>
/// <param name="Type"><c>oidc</c> or <c>local</c>.</param>
/// <param name="DisplayName">Button label.</param>
public sealed record LoginProviderInfo(string Name, string Type, string DisplayName);
