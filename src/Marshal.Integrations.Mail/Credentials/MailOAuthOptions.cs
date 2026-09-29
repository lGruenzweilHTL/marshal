using Marshal.Abstractions.Secrets;

namespace Marshal.Integrations.Mail.Credentials;

/// <summary>
/// OAuth clients for connecting mail accounts (<c>oauth</c> in <c>marshal.yaml</c>). Registering a Google Cloud or
/// Microsoft Entra app is the instance admin's job; without a client the matching connect button is disabled.
/// </summary>
public sealed class MailOAuthOptions
{
    public OAuthClientOptions? Google { get; set; }

    public OAuthClientOptions? Microsoft { get; set; }
}

/// <summary>An OAuth client registration.</summary>
public sealed class OAuthClientOptions
{
    public string ClientId { get; set; } = "";

    /// <summary>Name of the secret holding the client secret.</summary>
    public string? ClientCredentialSecret { get; set; }

    /// <summary>The client secret, resolved from the secret store at startup.</summary>
    [Secret(nameof(ClientCredentialSecret))]
    public string? ClientSecret { get; set; }

    internal bool IsConfigured => ClientId.Length > 0 && !string.IsNullOrEmpty(ClientSecret);
}
