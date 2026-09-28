using Marshal.Abstractions;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Identity;

namespace Marshal.Core.Context;

/// <summary>
/// Names the user from the owner's profile, never from a constant.
/// </summary>
public sealed class UserProfileContributor(IUserProfileProvider profiles) : IContextContributor
{
    public int Order => 100;

    public async Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        Result<UserProfile> profile = await profiles.GetAsync(request.Context.OwnerId, cancellationToken);
        return profile.IsSuccess ? $"You are assisting {profile.Value.DisplayName}." : null;
    }
}
