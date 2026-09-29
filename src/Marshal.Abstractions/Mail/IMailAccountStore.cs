namespace Marshal.Abstractions.Mail;

/// <summary>
/// The current owner's mail accounts (owner from the execution context).
/// </summary>
public interface IMailAccountStore
{
    Task<IReadOnlyList<MailAccount>> ListAsync(CancellationToken cancellationToken);

    Task<MailAccount?> GetAsync(MailAccountId id, CancellationToken cancellationToken);

    /// <summary>The account with this alias (case-insensitive), or null.</summary>
    Task<MailAccount?> FindByAliasAsync(string alias, CancellationToken cancellationToken);

    /// <summary>Creates an account; <c>mail.alias_taken</c> when the alias is in use.</summary>
    Task<Result<MailAccount>> CreateAsync(NewMailAccount account, CancellationToken cancellationToken);

    /// <summary>Deletes an account (its secret is the caller's to remove); <c>mail.account_not_found</c> otherwise.</summary>
    Task<Result> DeleteAsync(MailAccountId id, CancellationToken cancellationToken);
}

/// <summary>A new account.</summary>
public sealed record NewMailAccount(string Alias, MailProviderKind Provider, MailCredentialStrategy Strategy, string CredentialSecret, MailAccountSettings Settings);
