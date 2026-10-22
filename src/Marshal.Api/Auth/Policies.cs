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

    /// <summary>A logged-in human, or a machine token with <c>admin</c> (e.g. a monitoring script).</summary>
    public const string HumanOrAdmin = "human-or-admin";

    public static void AddScopePolicies(this AuthorizationOptions options)
    {
        foreach (string scope in ApiScopes.All)
            options.AddPolicy(scope, policy => policy.RequireAuthenticatedUser().RequireClaim(MarshalClaims.Scope, scope));

        options.AddPolicy(Human, policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identity?.AuthenticationType == MarshalAuthSchemes.Cookie));

        options.AddPolicy(HumanOrAdmin, policy => policy.RequireAuthenticatedUser()
            .RequireAssertion(context => context.User.Identity?.AuthenticationType == MarshalAuthSchemes.Cookie
                || context.User.HasClaim(MarshalClaims.Scope, ApiScopes.Admin)));
    }

    /// <summary>
    /// The credentials the named policy accepts (<see langword="null"/>: the default policy, any authenticated caller).
    /// Keep in step with <see cref="AddScopePolicies"/>: the OpenAPI document fails to generate for a policy missing here.
    /// </summary>
    public static PolicyCredentials Credentials(string? policyName) => policyName switch
    {
        null => new PolicyCredentials(Session: true, TokenScopes: []),
        Human => new PolicyCredentials(Session: true, TokenScopes: null),
        HumanOrAdmin => new PolicyCredentials(Session: true, TokenScopes: [ApiScopes.Admin]),
        _ when ApiScopes.All.Contains(policyName) => new PolicyCredentials(Session: true, TokenScopes: [policyName]),
        _ => throw new InvalidOperationException($"Authorization policy '{policyName}' has no credential mapping in Policies.Credentials."),
    };

    /// <summary>The credentials an inline policy accepts, from its authentication schemes; only token-scheme policies are known.</summary>
    public static PolicyCredentials Credentials(AuthorizationPolicy policy) =>
        policy.AuthenticationSchemes is [MarshalAuthSchemes.ApiToken]
            ? new PolicyCredentials(Session: false, TokenScopes: [])
            : throw new InvalidOperationException(
                $"Authorization policy for schemes '{string.Join(", ", policy.AuthenticationSchemes)}' has no credential mapping in Policies.Credentials.");
}
