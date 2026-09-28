namespace Marshal.Abstractions.Identity;

/// <summary>
/// Resolves the owner of a new session from identity evidence. Called once per session; the owner is then pinned.
/// </summary>
public interface IIdentityResolver
{
    Task<IdentityResolution> ResolveAsync(IdentityEvidence evidence, CancellationToken cancellationToken);
}
