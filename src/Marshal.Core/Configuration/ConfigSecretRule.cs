namespace Marshal.Core.Configuration;

/// <summary>
/// Why a configuration entry looks like a literal secret.
/// </summary>
public enum ConfigSecretRule
{
    /// <summary>A reference key (<c>...Secret</c>) whose value is not a secret name.</summary>
    InvalidReference,

    /// <summary>A key whose last word names a credential (<c>password</c>, <c>apiKey</c>, ...).</summary>
    ForbiddenKey,

    /// <summary>A value that looks like a credential, whatever its key.</summary>
    CredentialValue,
}
