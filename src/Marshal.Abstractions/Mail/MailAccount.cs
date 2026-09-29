using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Mail;

/// <summary>
/// A mailbox of one user: which protocol (<see cref="Provider"/>) and which credentials (<see cref="Strategy"/>) reach
/// it, and under which alias (e.g. "private", "work") tools address it.
/// </summary>
/// <param name="Id">Account id.</param>
/// <param name="Owner">The user the mailbox belongs to.</param>
/// <param name="Alias">Short name used as the tools' <c>account</c> argument (lowercase letters, digits, '-').</param>
/// <param name="Provider">Protocol.</param>
/// <param name="Strategy">Credential strategy.</param>
/// <param name="CredentialSecret">Secret name holding the password or the OAuth refresh token (an owner secret).</param>
/// <param name="Settings">Server and user name.</param>
/// <param name="CreatedAt">Creation time.</param>
public sealed record MailAccount(
    MailAccountId Id,
    OwnerId Owner,
    string Alias,
    MailProviderKind Provider,
    MailCredentialStrategy Strategy,
    string CredentialSecret,
    MailAccountSettings Settings,
    DateTimeOffset CreatedAt);

/// <summary>Protocol of a mail account.</summary>
public enum MailProviderKind
{
    Imap,
}

/// <summary>How a mail account authenticates.</summary>
public enum MailCredentialStrategy
{
    /// <summary>Password or app password.</summary>
    Password,

    /// <summary>OAuth 2 with Google (XOAUTH2).</summary>
    OAuthGoogle,

    /// <summary>OAuth 2 with Microsoft (XOAUTH2).</summary>
    OAuthMicrosoft,
}

/// <summary>Server settings of a mail account.</summary>
/// <param name="Host">IMAP host.</param>
/// <param name="Port">IMAP port (993 for implicit TLS, 143 for STARTTLS).</param>
/// <param name="Security">Transport security.</param>
/// <param name="UserName">Login name (usually the address).</param>
/// <param name="DraftsFolder">Drafts folder when the server marks none with <c>\Drafts</c>.</param>
public sealed record MailAccountSettings(string Host, int Port, MailSecurity Security, string UserName, string? DraftsFolder = null);

/// <summary>Transport security of a mail connection.</summary>
public enum MailSecurity
{
    /// <summary>TLS from the start (port 993).</summary>
    Tls,

    /// <summary>Plain connection upgraded with STARTTLS (port 143).</summary>
    StartTls,

    /// <summary>No encryption; only for test servers on a trusted network.</summary>
    None,
}
