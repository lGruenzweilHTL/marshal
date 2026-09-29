using Marshal.Abstractions;
using Marshal.Abstractions.Identity;

namespace Marshal.Tasks;

internal static class OwnerTimeZones
{
    /// <summary>The owner's time zone; UTC when the profile or its zone id cannot be resolved.</summary>
    public static async Task<TimeZoneInfo> TimeZoneAsync(this IUserProfileProvider profiles, OwnerId owner, CancellationToken cancellationToken)
    {
        Result<UserProfile> profile = await profiles.GetAsync(owner, cancellationToken);
        return profile.IsSuccess && TimeZoneInfo.TryFindSystemTimeZoneById(profile.Value.TimeZone, out TimeZoneInfo? zone) ? zone : TimeZoneInfo.Utc;
    }
}
