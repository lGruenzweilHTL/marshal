using Microsoft.AspNetCore.Authentication;

namespace Marshal.Api.Auth;

/// <summary>
/// Starts the authorization code + PKCE flow of the OIDC handler registered under the provider's name.
/// </summary>
internal sealed class OidcLoginProvider(string name, string displayName) : ILoginProvider
{
    public string Name => name;

    public string Type => LoginProviderOptions.OidcType;

    public string DisplayName => displayName;

    public IResult Start(HttpContext context, string returnUrl) =>
        TypedResults.Challenge(new AuthenticationProperties { RedirectUri = context.Request.PathBase + returnUrl }, [name]);
}
