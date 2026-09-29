using System.Globalization;
using System.Security.Cryptography;
using Marshal.Abstractions.Identity;
using Marshal.Data.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Marshal.Data.Persistence;

/// <summary>
/// Applies pending migrations at startup (after copying the database file) and seeds the instance owner.
/// </summary>
public sealed partial class DatabaseInitializer(MarshalDbContext db, TimeProvider timeProvider, ILogger<DatabaseInitializer> logger)
{
    public async Task InitializeAsync(InstanceOwnerSeed seed, CancellationToken ct)
    {
        List<string> pending = [.. await db.Database.GetPendingMigrationsAsync(ct)];
        if (pending.Count > 0)
        {
            await BackupDatabaseFileAsync(ct);
            await db.Database.MigrateAsync(ct);
            LogMigrated(logger, pending.Count);
        }

        await SeedInstanceOwnerAsync(seed, ct);
    }

    private async Task BackupDatabaseFileAsync(CancellationToken ct)
    {
        string? path = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        if (string.IsNullOrEmpty(path) || path == ":memory:" || !File.Exists(path) || new FileInfo(path).Length == 0)
            return;

        // Databases use the write-ahead log; committed pages still in it are moved into the file before copying it.
        await db.Database.ExecuteSqlRawAsync("PRAGMA wal_checkpoint(TRUNCATE)", ct);
        SqliteConnection.ClearAllPools();
        string stamp = timeProvider.GetUtcNow().ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture);
        string backup = $"{path}.{stamp}.bak";
        File.Copy(path, backup, overwrite: false);
        LogBackedUp(logger, backup);
    }

    private async Task SeedInstanceOwnerAsync(InstanceOwnerSeed seed, CancellationToken ct)
    {
        if (await db.Users.AnyAsync(ct))
            return;

        var owner = new User
        {
            Id = OwnerId.New(),
            UserName = "owner",
            NormalizedUserName = "OWNER",
            DisplayName = seed.DisplayName,
            SecurityStamp = Convert.ToHexString(RandomNumberGenerator.GetBytes(16)),
            CreatedAt = timeProvider.GetUtcNow(),
        };
        db.Users.Add(owner);
        db.UserPreferences.Add(new UserPreferences { UserId = owner.Id, Language = seed.Language, TimeZone = seed.TimeZone });
        await db.SaveChangesAsync(ct);
        LogSeeded(logger, owner.Id.Value);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Applied {Count} pending database migrations")]
    private static partial void LogMigrated(ILogger logger, int count);

    [LoggerMessage(Level = LogLevel.Information, Message = "Copied database to {BackupPath} before migrating")]
    private static partial void LogBackedUp(ILogger logger, string backupPath);

    [LoggerMessage(Level = LogLevel.Information, Message = "Created instance owner {OwnerId}")]
    private static partial void LogSeeded(ILogger logger, Guid ownerId);
}
