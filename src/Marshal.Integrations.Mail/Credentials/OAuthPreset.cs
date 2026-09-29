using Marshal.Abstractions.Mail;

namespace Marshal.Integrations.Mail.Credentials;

/// <summary>Endpoints and scopes of an OAuth provider for IMAP access.</summary>
/// <param name="Name">Provider name used in the callback path (<c>/oauth/callback/{name}</c>).</param>
/// <param name="Strategy">Credential strategy of accounts connected this way.</param>
/// <param name="AuthorizeEndpoint">Authorization endpoint.</param>
/// <param name="TokenEndpoint">Token endpoint.</param>
/// <param name="Scope">Space-separated scopes.</param>
/// <param name="AuthorizeParameters">Extra authorization parameters.</param>
/// <param name="ScopeOnRefresh">Whether refresh requests repeat the scope (Microsoft).</param>
/// <param name="ImapHost">IMAP server of accounts connected this way (implicit TLS on port 993).</param>
public sealed record OAuthPreset(
    string Name,
    MailCredentialStrategy Strategy,
    Uri AuthorizeEndpoint,
    Uri TokenEndpoint,
    string Scope,
    IReadOnlyDictionary<string, string> AuthorizeParameters,
    bool ScopeOnRefresh,
    string ImapHost);

/// <summary>The supported OAuth providers (verified against their documentation, see the integrations design).</summary>
public static class OAuthPresets
{
    /// <summary>Google: offline access and forced consent so a refresh token is always issued.</summary>
    public static readonly OAuthPreset Google = new(
        "google",
        MailCredentialStrategy.OAuthGoogle,
        new Uri("https://accounts.google.com/o/oauth2/v2/auth"),
        new Uri("https://oauth2.googleapis.com/token"),
        "https://mail.google.com/",
        new Dictionary<string, string> { ["access_type"] = "offline", ["prompt"] = "consent" },
        ScopeOnRefresh: false,
        ImapHost: "imap.gmail.com");

    /// <summary>Microsoft 365 and Outlook.com through the <c>common</c> endpoints.</summary>
    public static readonly OAuthPreset Microsoft = new(
        "microsoft",
        MailCredentialStrategy.OAuthMicrosoft,
        new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/authorize"),
        new Uri("https://login.microsoftonline.com/common/oauth2/v2.0/token"),
        "https://outlook.office.com/IMAP.AccessAsUser.All offline_access",
        new Dictionary<string, string> { ["prompt"] = "select_account" },
        ScopeOnRefresh: true,
        ImapHost: "outlook.office365.com");

    public static IReadOnlyList<OAuthPreset> All { get; } = [Google, Microsoft];

    public static OAuthPreset? Find(string name) => All.FirstOrDefault(preset => preset.Name == name);
}
