using System.Security.Claims;
using Marshal.Abstractions;

namespace Marshal.Api.Auth;

/// <summary>
/// Maps the provider's group claim values through <c>roleMappings</c>; a login without a mapped group is denied.
/// </summary>
internal sealed class GroupClaimRoleProvisioner : IRoleProvisioner
{
    public Result<IReadOnlyList<string>> RolesFor(LoginProviderOptions provider, ClaimsPrincipal external)
    {
        List<string> roles = [.. external.FindAll(provider.Claims.Groups)
            .Select(claim => provider.RoleMappings.GetValueOrDefault(claim.Value))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        return roles.Count > 0
            ? roles
            : new Error("auth.no_mapped_group", "The login has no group that maps to a Marshal role.", ErrorType.Forbidden);
    }
}
