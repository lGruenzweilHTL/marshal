using System.Text.Json;
using Marshal.Abstractions.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Identity;

/// <summary>
/// Preferences in <c>user_preferences</c>; quiet hours and notification targets are JSON columns.
/// </summary>
public sealed class EfUserPreferencesStore(IDbContextFactory<MarshalDbContext> contexts, IInstanceTimeZone instanceTimeZone) : IUserPreferencesStore
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

    public async Task<UserPreferencesRecord> GetAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        UserPreferences? row = await db.UserPreferences.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.UserId == owner, cancellationToken);
        if (row is null)
            return new UserPreferencesRecord("en", await instanceTimeZone.GetAsync(cancellationToken), [], NotificationTargets.None, null);

        return new UserPreferencesRecord(
            row.Language,
            row.TimeZone,
            row.QuietHours is null ? [] : JsonSerializer.Deserialize<List<QuietHoursWindow>>(row.QuietHours, _json) ?? [],
            row.NotificationTargets is null ? NotificationTargets.None : JsonSerializer.Deserialize<NotificationTargets>(row.NotificationTargets, _json) ?? NotificationTargets.None,
            row.DndUntil,
            row.HelpMarshalLearn);
    }

    public async Task SaveAsync(OwnerId owner, UserPreferencesRecord preferences, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        UserPreferences? row = await db.UserPreferences.SingleOrDefaultAsync(candidate => candidate.UserId == owner, cancellationToken);
        if (row is null)
        {
            row = new UserPreferences { UserId = owner, Language = preferences.Language, TimeZone = preferences.TimeZone };
            db.UserPreferences.Add(row);
        }

        row.Language = preferences.Language;
        row.TimeZone = preferences.TimeZone;
        row.QuietHours = JsonSerializer.Serialize(preferences.QuietHours, _json);
        row.NotificationTargets = JsonSerializer.Serialize(preferences.NotificationTargets, _json);
        row.DndUntil = preferences.DndUntil;
        row.HelpMarshalLearn = preferences.HelpMarshalLearn;
        await db.SaveChangesAsync(cancellationToken);
    }
}
