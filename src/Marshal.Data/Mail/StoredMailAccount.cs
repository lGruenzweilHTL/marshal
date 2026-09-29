using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Mail;

namespace Marshal.Data.Mail;

/// <summary>A user's mail account. Credentials live in the owner secret named by <see cref="CredentialSecret"/>.</summary>
public sealed class StoredMailAccount : IOwned
{
    public required MailAccountId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    /// <summary>Lowercase alias, unique per owner.</summary>
    public required string Alias { get; init; }

    public required MailProviderKind Provider { get; init; }

    public required MailCredentialStrategy Strategy { get; init; }

    public required string CredentialSecret { get; init; }

    public required string Host { get; init; }

    public required int Port { get; init; }

    public required MailSecurity Security { get; init; }

    public required string UserName { get; init; }

    public string? DraftsFolder { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
