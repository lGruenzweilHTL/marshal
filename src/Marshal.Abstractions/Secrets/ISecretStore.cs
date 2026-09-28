using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Secrets;

/// <summary>
/// Resolves secret values by name. The only way code obtains a secret value.
/// </summary>
public interface ISecretStore
{
    /// <summary>
    /// Resolves a name referenced from instance configuration (<c>/config</c>):
    /// environment / files first, then instance database rows. Never a user's rows.
    /// </summary>
    /// <returns>The value, or a failure with code <c>secret.not_found</c>.</returns>
    Task<Result<string>> GetAsync(SecretName name, CancellationToken cancellationToken);

    /// <summary>
    /// Resolves a name referenced from a row owned by <paramref name="owner"/>:
    /// environment / files, then the owner's database rows, then instance database rows.
    /// </summary>
    /// <returns>The value, or a failure with code <c>secret.not_found</c>.</returns>
    Task<Result<string>> GetForOwnerAsync(SecretName name, OwnerId owner, CancellationToken cancellationToken);
}
