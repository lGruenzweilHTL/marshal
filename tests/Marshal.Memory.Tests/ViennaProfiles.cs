using Marshal.Abstractions;
using Marshal.Abstractions.Identity;

namespace Marshal.Memory.Tests;

/// <summary>Every owner lives in Vienna and speaks English.</summary>
internal sealed class ViennaProfiles : IUserProfileProvider
{
    public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
        Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Alice", "en", "Europe/Vienna"));
}
