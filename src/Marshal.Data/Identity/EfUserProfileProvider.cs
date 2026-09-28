using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Identity;

/// <summary>
/// Profiles from the users and user_preferences tables.
/// </summary>
public sealed class EfUserProfileProvider(IDbContextFactory<MarshalDbContext> contexts) : IUserProfileProvider
{
    public async Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        User? user = await db.Users.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == owner, cancellationToken);
        if (user is null)
            return new Error("user.not_found", "The user does not exist.", ErrorType.NotFound);

        UserPreferences? preferences = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.UserId == owner, cancellationToken);
        return new UserProfile(owner, user.DisplayName, preferences?.Language ?? "en", preferences?.TimeZone ?? "UTC");
    }
}
