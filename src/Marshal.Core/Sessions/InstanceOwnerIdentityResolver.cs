using Marshal.Abstractions.Identity;

namespace Marshal.Core.Sessions;

/// <summary>
/// v1 identity resolver: every session belongs to the instance owner. The evidence is still stored.
/// </summary>
public sealed class InstanceOwnerIdentityResolver(IInstanceOwner instanceOwner) : IIdentityResolver
{
    public const string ResolvedBy = "owner_default";

    public async Task<IdentityResolution> ResolveAsync(IdentityEvidence evidence, CancellationToken cancellationToken) =>
        new(await instanceOwner.GetAsync(cancellationToken), ResolvedBy);
}
