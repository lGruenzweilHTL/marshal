using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;

namespace Marshal.Api.Auth;

/// <summary>
/// Gives cookie principals the scopes their roles grant, so cookie and token principals share the scope policies.
/// Token principals keep exactly the scopes of their token.
/// </summary>
internal sealed class ScopeClaimsTransformation : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { AuthenticationType: MarshalAuthSchemes.Cookie } || principal.HasClaim(claim => claim.Type == MarshalClaims.Scope))
            return Task.FromResult(principal);

        ClaimsPrincipal transformed = principal.Clone();
        var identity = (ClaimsIdentity)transformed.Identity!;
        foreach (string scope in Roles.ScopesOf(principal.FindAll(identity.RoleClaimType).Select(claim => claim.Value)))
            identity.AddClaim(new Claim(MarshalClaims.Scope, scope));

        return Task.FromResult(transformed);
    }
}
