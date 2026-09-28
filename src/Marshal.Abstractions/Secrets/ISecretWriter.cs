using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Secrets;

/// <summary>
/// Writes UI-managed secrets. Values go in, never out.
/// </summary>
public interface ISecretWriter
{
    /// <summary>Creates or replaces a secret; <paramref name="owner"/> null writes an instance secret.</summary>
    Task SetAsync(SecretName name, string value, OwnerId? owner, CancellationToken cancellationToken);

    /// <summary>Deletes a secret; fails with <c>secret.not_found</c> when it does not exist.</summary>
    Task<Result> DeleteAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken);
}
