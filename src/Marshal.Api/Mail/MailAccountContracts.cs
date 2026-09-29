using Marshal.Abstractions.Mail;

namespace Marshal.Api.Mail;

/// <summary>A mail account without its credentials.</summary>
/// <param name="Id">Account id.</param>
/// <param name="Alias">Alias used as the tools' <c>account</c> argument.</param>
/// <param name="Provider">Protocol (<c>imap</c>).</param>
/// <param name="Strategy">Credential strategy: <c>password</c>, <c>oauth_google</c> or <c>oauth_microsoft</c>.</param>
/// <param name="Host">IMAP host.</param>
/// <param name="Port">IMAP port.</param>
/// <param name="Security">Transport security.</param>
/// <param name="UserName">Login name.</param>
/// <param name="DraftsFolder">Configured drafts folder, if any.</param>
/// <param name="CreatedAt">Creation time.</param>
public sealed record MailAccountItem(
    string Id, string Alias, string Provider, string Strategy, string Host, int Port, MailSecurity Security, string UserName, string? DraftsFolder, DateTimeOffset CreatedAt)
{
    internal static MailAccountItem From(MailAccount account) => new(
        account.Id.ToString(),
        account.Alias,
        "imap",
        account.Strategy switch
        {
            MailCredentialStrategy.OAuthGoogle => "oauth_google",
            MailCredentialStrategy.OAuthMicrosoft => "oauth_microsoft",
            _ => "password",
        },
        account.Settings.Host,
        account.Settings.Port,
        account.Settings.Security,
        account.Settings.UserName,
        account.Settings.DraftsFolder,
        account.CreatedAt);
}

/// <summary>A new IMAP account with a password or app password. The password becomes a write-only owner secret.</summary>
/// <param name="Alias">1-32 lowercase letters, digits or '-'.</param>
/// <param name="Host">IMAP host.</param>
/// <param name="Port">IMAP port (993 for TLS, 143 for STARTTLS).</param>
/// <param name="Security">Transport security.</param>
/// <param name="UserName">Login name.</param>
/// <param name="Password">Password or app password.</param>
/// <param name="DraftsFolder">Drafts folder when the server marks none with <c>\Drafts</c>.</param>
public sealed record CreateMailAccountRequest(string Alias, string Host, int Port, MailSecurity Security, string UserName, string Password, string? DraftsFolder = null);

/// <summary>An OAuth provider and whether this instance has a client for it.</summary>
public sealed record MailOAuthProvider(string Name, bool Configured);

/// <summary>Starts connecting an account through OAuth.</summary>
/// <param name="Alias">Alias of the new account.</param>
/// <param name="Email">The mailbox address (the IMAP login name).</param>
public sealed record StartMailOAuthRequest(string Alias, string Email);

/// <summary>Where the browser goes to sign in with the provider.</summary>
public sealed record StartMailOAuthResponse(Uri AuthorizationUrl);
