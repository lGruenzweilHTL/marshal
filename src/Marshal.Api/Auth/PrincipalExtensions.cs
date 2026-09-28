using System.Security.Claims;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Identity;

namespace Marshal.Api.Auth;

/// <summary>
/// Reading Marshal's claims from an authenticated principal.
/// </summary>
internal static class PrincipalExtensions
{
    public static OwnerId Owner(this ClaimsPrincipal principal) => OwnerId.Parse(principal.FindFirstValue(MarshalClaims.Owner)!, null);

    public static IReadOnlySet<string> Scopes(this ClaimsPrincipal principal) =>
        principal.FindAll(MarshalClaims.Scope).Select(claim => claim.Value).ToHashSet(StringComparer.Ordinal);

    /// <summary>The audit actor: the token for token principals, the user otherwise.</summary>
    public static (AuditActorKind Kind, string Id) Actor(this ClaimsPrincipal principal) =>
        principal.FindFirstValue(MarshalClaims.TokenId) is { } token
            ? (AuditActorKind.ApiToken, token)
            : (AuditActorKind.User, principal.FindFirstValue(MarshalClaims.Owner)!);
}
