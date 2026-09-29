namespace Marshal.Abstractions.Mail;

/// <summary>Stable mail error codes.</summary>
public static class MailErrors
{
    public static Error CredentialMissing(string alias) =>
        new("mail.credential_missing", $"The credentials of mail account '{alias}' are missing.", ErrorType.Unavailable);

    public static Error ReauthorizeRequired(string alias) =>
        new("mail.reauthorize_required", $"Mail account '{alias}' must be connected again in the accounts view.", ErrorType.Unavailable);

    public static Error OAuthNotConfigured(string provider) =>
        new("mail.oauth_not_configured", $"OAuth for {provider} is not configured on this instance.", ErrorType.Unavailable);

    public static Error AccountNotFound(string alias) => new("mail.account_not_found", $"There is no mail account '{alias}'.", ErrorType.NotFound);

    public static Error AliasTaken(string alias) => new("mail.alias_taken", $"The alias '{alias}' is already used by another mail account.", ErrorType.Conflict);

    public static Error MessageNotFound(string id) => new("mail.message_not_found", $"There is no message '{id}'.", ErrorType.NotFound);

    public static Error DraftsFolderMissing(string alias) =>
        new("mail.drafts_folder_missing", $"Mail account '{alias}' has no folder marked as drafts; name one in the account settings.", ErrorType.Failure);

    public static Error AuthFailed(string alias) =>
        new("mail.auth_failed", $"The mail server rejected the login of account '{alias}'.", ErrorType.Unavailable);

    public static Error Unreachable(string alias, string reason) =>
        new("mail.unreachable", $"The mail server of account '{alias}' could not be reached: {reason}", ErrorType.Unavailable);

    public static Error OAuthFailed(string message) => new("mail.oauth_failed", message, ErrorType.Unavailable);
}
