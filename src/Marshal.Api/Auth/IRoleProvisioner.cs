using System.Security.Claims;
using Marshal.Abstractions;

namespace Marshal.Api.Auth;

/// <summary>
/// Derives Marshal roles from an external login's claims. Fails (forbidden) when no role applies.
/// </summary>
internal interface IRoleProvisioner
{
    Result<IReadOnlyList<string>> RolesFor(LoginProviderOptions provider, ClaimsPrincipal external);
}
