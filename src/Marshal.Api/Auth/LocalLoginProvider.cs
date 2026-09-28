namespace Marshal.Api.Auth;

/// <summary>
/// Local accounts (passkeys, password fallback): login happens in the SPA's login page.
/// </summary>
internal sealed class LocalLoginProvider(string name, string displayName) : ILoginProvider
{
    public string Name => name;

    public string Type => LoginProviderOptions.LocalType;

    public string DisplayName => displayName;

    public IResult Start(HttpContext context, string returnUrl) =>
        TypedResults.Redirect($"{context.Request.PathBase}/login/{name}?returnUrl={Uri.EscapeDataString(returnUrl)}");
}
