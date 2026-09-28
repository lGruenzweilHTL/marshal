using Marshal.Abstractions.Tokens;
using Microsoft.AspNetCore.Authorization;

namespace Marshal.Api.Auth;

/// <summary>
/// One authorization policy per scope, named after the scope.
/// </summary>
internal static class Policies
{
    public const string ConversationWrite = ApiScopes.ConversationWrite;

    public static void AddScopePolicies(this AuthorizationOptions options)
    {
        foreach (string scope in ApiScopes.All)
            options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireClaim(MarshalClaims.Scope, scope));
    }
}
