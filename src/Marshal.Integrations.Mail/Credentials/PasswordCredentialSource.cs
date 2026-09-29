using Marshal.Abstractions;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Secrets;

namespace Marshal.Integrations.Mail.Credentials;

/// <summary>Password or app password from the account's owner secret.</summary>
public sealed class PasswordCredentialSource(ISecretStore secrets) : ICredentialSource
{
    public MailCredentialStrategy Strategy => MailCredentialStrategy.Password;

    public async Task<Result<MailCredential>> GetAsync(MailAccount account, CancellationToken cancellationToken)
    {
        Result<SecretName> name = SecretName.Create(account.CredentialSecret);
        if (name.IsFailure)
            return MailErrors.CredentialMissing(account.Alias);

        Result<string> password = await secrets.GetForOwnerAsync(name.Value, account.Owner, cancellationToken);
        return password.IsSuccess
            ? new MailCredential(account.Settings.UserName, password.Value, IsAccessToken: false)
            : MailErrors.CredentialMissing(account.Alias);
    }
}
