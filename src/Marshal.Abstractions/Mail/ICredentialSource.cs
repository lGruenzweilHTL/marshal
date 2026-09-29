namespace Marshal.Abstractions.Mail;

/// <summary>
/// Supplies the login for a mail account; one implementation per <see cref="MailCredentialStrategy"/>.
/// </summary>
public interface ICredentialSource
{
    MailCredentialStrategy Strategy { get; }

    /// <summary>
    /// The account's current credential. OAuth sources refresh the access token as needed;
    /// <c>mail.reauthorize_required</c> when the refresh token was revoked.
    /// </summary>
    Task<Result<MailCredential>> GetAsync(MailAccount account, CancellationToken cancellationToken);
}

/// <summary>A login: a password, or an OAuth access token for SASL XOAUTH2.</summary>
/// <param name="UserName">Login name.</param>
/// <param name="Secret">Password or access token.</param>
/// <param name="IsAccessToken">True for an OAuth access token.</param>
public sealed record MailCredential(string UserName, string Secret, bool IsAccessToken);
