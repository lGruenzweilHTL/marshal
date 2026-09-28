using Marshal.Abstractions.Secrets;

namespace Marshal.Api.Auth;

/// <summary>
/// Human login (<c>auth</c> in <c>marshal.yaml</c>). Changes need a restart.
/// </summary>
public sealed class AuthOptions
{
    /// <summary>Active login providers; several may be active at once.</summary>
    public List<LoginProviderOptions> Providers { get; set; } = [];
}

/// <summary>
/// One login provider (<c>auth.providers[i]</c>).
/// </summary>
public sealed class LoginProviderOptions
{
    public const string OidcType = "oidc";
    public const string LocalType = "local";

    /// <summary>Stable id used in URLs (<c>/api/v1/auth/login/{name}</c>, <c>/signin-oidc/{name}</c>).</summary>
    public string Name { get; set; } = "";

    /// <summary><c>oidc</c> or <c>local</c>.</summary>
    public string Type { get; set; } = "";

    /// <summary>Button label in the login page.</summary>
    public string? DisplayName { get; set; }

    /// <summary>OIDC issuer URL (discovery document at <c>.well-known/openid-configuration</c>).</summary>
    public Uri? Authority { get; set; }

    public string? ClientId { get; set; }

    /// <summary>Name of the secret holding the OIDC client secret.</summary>
    public string? ClientCredentialSecret { get; set; }

    /// <summary>The client secret, resolved from the secret store at startup.</summary>
    [Secret(nameof(ClientCredentialSecret))]
    public string? ClientSecret { get; set; }

    public List<string> Scopes { get; set; } = ["openid", "profile", "email"];

    /// <summary>Also read claims from the userinfo endpoint (for IdPs that keep groups out of the ID token).</summary>
    public bool UseUserInfo { get; set; }

    public ClaimMappingOptions Claims { get; set; } = new();

    /// <summary>IdP group → Marshal role (<c>admin</c> or <c>user</c>). A login without a mapped group is denied.</summary>
    public Dictionary<string, string> RoleMappings { get; set; } = [];

    /// <summary>
    /// <c>local</c> only: WebAuthn relying party id (e.g. <c>marshal.example.com</c>). Unset, the request host is used,
    /// so set it when the host header is not validated by the reverse proxy.
    /// </summary>
    public string? ServerDomain { get; set; }

    /// <summary>The label shown when <see cref="DisplayName"/> is not set.</summary>
    public string EffectiveDisplayName => DisplayName ?? (Type == LocalType ? "Local account" : Name);
}

/// <summary>
/// Which ID token claims carry the subject, the display name and the groups.
/// </summary>
public sealed class ClaimMappingOptions
{
    public string Subject { get; set; } = "sub";

    public string Name { get; set; } = "name";

    public string Groups { get; set; } = "groups";
}
