namespace Marshal.Api.Auth;

/// <summary>
/// One configured login provider: listed by <c>/api/v1/auth/providers</c> and started by
/// <c>/api/v1/auth/login/{name}</c>.
/// </summary>
internal interface ILoginProvider
{
    string Name { get; }

    /// <summary><c>oidc</c> or <c>local</c>.</summary>
    string Type { get; }

    string DisplayName { get; }

    /// <summary>Starts a login that ends at <paramref name="returnUrl"/> (app-relative, already validated).</summary>
    IResult Start(HttpContext context, string returnUrl);
}
