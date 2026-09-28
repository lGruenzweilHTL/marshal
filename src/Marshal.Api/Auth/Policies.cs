using Marshal.Abstractions.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace Marshal.Api.Auth;

/// <summary>
/// One authorization policy per scope, named after the scope.
/// </summary>
internal static class Policies
{
    public const string ConversationWrite = ApiScopes.ConversationWrite;

    public const string Admin = ApiScopes.Admin;

    /// <summary>A logged-in human (cookie session). Credential management is not available to machine tokens.</summary>
    public const string Human = "human";

    public static void AddScopePolicies(this AuthorizationOptions options)
    {
        foreach (string scope in ApiScopes.All)
            options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireClaim(MarshalClaims.Scope, scope));

        options.AddPolicy(Human, policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identity?.AuthenticationType == MarshalAuthSchemes.Cookie));
    }
}
