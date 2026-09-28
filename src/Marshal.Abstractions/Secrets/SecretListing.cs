using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Secrets;

/// <summary>Where a secret value lives.</summary>
public enum SecretOrigin
{
    /// <summary><c>MARSHAL_SECRET_*</c> variable or <c>/run/secrets</c> file (operator-managed, read-only).</summary>
    Environment,

    /// <summary>Encrypted database row (managed in the UI).</summary>
    Database,
}

/// <summary>
/// A secret without its value.
/// </summary>
/// <param name="Name">The name.</param>
/// <param name="Owner">The owning user; null for instance secrets.</param>
/// <param name="Origin">Where the value lives.</param>
/// <param name="UpdatedAt">Last change (database secrets only).</param>
public sealed record SecretListing(SecretName Name, OwnerId? Owner, SecretOrigin Origin, DateTimeOffset? UpdatedAt);
