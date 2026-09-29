using Marshal.Abstractions;
using Marshal.Abstractions.Identity;

namespace Marshal.Tasks.Tests;

/// <summary>
/// Every owner is "Alice" in Vienna, speaking German.
/// </summary>
internal sealed class ViennaProfiles : IUserProfileProvider
{
    public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
        Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Alice", "de", "Europe/Vienna"));
}
