using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Secrets;

/// <summary>
/// A place that names a secret.
/// </summary>
/// <param name="Name">The referenced secret.</param>
/// <param name="Location">Where it is referenced, e.g. <c>marshal.yaml: models.claude.apiKeySecret</c>.</param>
/// <param name="OwnerScoped">True for references on user-owned rows (resolved with the owner's secrets).</param>
public sealed record SecretReference(SecretName Name, string Location, bool OwnerScoped);

/// <summary>
/// Lists secret references (configuration, user-owned rows) so unresolved ones can be flagged.
/// </summary>
public interface ISecretReferenceSource
{
    /// <summary>Instance references plus the references on <paramref name="owner"/>'s rows.</summary>
    Task<IReadOnlyList<SecretReference>> ListAsync(OwnerId owner, CancellationToken cancellationToken);
}
