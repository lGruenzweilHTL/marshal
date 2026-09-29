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

    public static Error OAuthFailed(string message) => new("mail.oauth_failed", message, ErrorType.Unavailable);
}
