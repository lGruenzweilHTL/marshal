namespace Marshal.Abstractions.Identity;

/// <summary>
/// Stores per-user preferences.
/// </summary>
public interface IUserPreferencesStore
{
    /// <summary>The user's preferences; defaults (English, instance time zone) when none are stored.</summary>
    Task<UserPreferencesRecord> GetAsync(OwnerId owner, CancellationToken cancellationToken);

    /// <summary>Replaces the user's preferences. Callers validate first.</summary>
    Task SaveAsync(OwnerId owner, UserPreferencesRecord preferences, CancellationToken cancellationToken);
}
