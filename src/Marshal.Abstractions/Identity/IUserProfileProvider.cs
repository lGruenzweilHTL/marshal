namespace Marshal.Abstractions.Identity;

/// <summary>
/// Reads a user's profile and preferences.
/// </summary>
public interface IUserProfileProvider
{
    /// <summary>The profile, or a failure with code <c>user.not_found</c>.</summary>
    Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken);
}
