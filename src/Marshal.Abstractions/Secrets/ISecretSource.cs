using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Secrets;

/// <summary>
/// One place secret values can come from (environment / files, database).
/// </summary>
public interface ISecretSource
{
    /// <summary>
    /// Looks up a secret. <paramref name="owner"/> null asks for an instance-scoped value;
    /// otherwise only that owner's value is returned. Sources without owners ignore it.
    /// </summary>
    /// <returns>The value, or null when this source does not have it.</returns>
    Task<string?> TryGetAsync(SecretName name, OwnerId? owner, CancellationToken cancellationToken);

    /// <summary>True when the source stores values per owner (the database); false for environment / files.</summary>
    bool IsOwnerScoped { get; }
}
