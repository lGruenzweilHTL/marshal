using Marshal.Abstractions.Identity;

namespace Marshal.Testing;

/// <summary>
/// Preferences in a dictionary; English in Vienna without quiet hours or targets by default.
/// </summary>
public sealed class FakeUserPreferencesStore : IUserPreferencesStore
{
    public Dictionary<OwnerId, UserPreferencesRecord> Stored { get; } = [];

    public Task<UserPreferencesRecord> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
        Task.FromResult(Stored.GetValueOrDefault(owner) ?? new UserPreferencesRecord("en", "Europe/Vienna", [], NotificationTargets.None, null));

    public Task SaveAsync(OwnerId owner, UserPreferencesRecord preferences, CancellationToken cancellationToken)
    {
        Stored[owner] = preferences;
        return Task.CompletedTask;
    }
}
