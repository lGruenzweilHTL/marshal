using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Secrets;

/// <summary>
/// Lists the secrets a source holds, never their values.
/// </summary>
public interface ISecretCatalog
{
    /// <summary>Instance secrets plus the secrets of <paramref name="owner"/>.</summary>
    Task<IReadOnlyList<SecretListing>> ListAsync(OwnerId owner, CancellationToken cancellationToken);
}
