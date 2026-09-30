using System.Globalization;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Configuration;
using Marshal.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Maintenance;

/// <summary>
/// Nightly backup (system job <c>backup</c>): a folder <c>/data/backups/&lt;yyyyMMdd-HHmmss&gt;</c> (UTC) with an
/// online copy of the database (<c>VACUUM INTO</c>), a copy of the Data Protection keys (without them the stored
/// secrets cannot be decrypted after a restore) and the memory export of every owner. Only the newest
/// <see cref="MaintenanceOptions.BackupCopies"/> dated folders are kept; other folders are never touched.
/// </summary>
public sealed partial class BackupJob(
    IDbContextFactory<MarshalDbContext> contexts,
    MemoryExportJob exports,
    IOptions<PathsOptions> paths,
    IOptions<MaintenanceOptions> options,
    TimeProvider time,
    ILogger<BackupJob> logger) : ISystemJob
{
    public const string JobName = "backup";

    private const string StampFormat = "yyyyMMdd-HHmmss";

    public string Name => JobName;

    public async Task<Result<string>> RunAsync(CancellationToken cancellationToken)
    {
        string backups = Path.Combine(paths.Value.Data, "backups");
        string stamp = time.GetUtcNow().UtcDateTime.ToString(StampFormat, CultureInfo.InvariantCulture);
        string folder = Path.Combine(backups, stamp);
        Directory.CreateDirectory(folder);

        string database = Path.Combine(folder, "marshal.db");
        await using (MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken))
            await db.Database.ExecuteSqlAsync($"VACUUM INTO {database}", cancellationToken);

        string keys = Path.Combine(paths.Value.Data, "keys");
        bool keysCopied = Directory.Exists(keys);
        if (keysCopied)
            CopyDirectory(keys, Path.Combine(folder, "keys"));
        else
            LogNoKeys(keys);

        int exported = await exports.ExportAllAsync(folder, cancellationToken);
        int deleted = Prune(backups);
        LogBackedUp(folder, deleted);
        return string.Create(CultureInfo.InvariantCulture,
            $"Backup {stamp}: database, {(keysCopied ? "keys" : "no keys (none yet)")} and {exported} memory exports; {deleted} older backups deleted.");
    }

    private int Prune(string backups)
    {
        string[] old = [.. Directory.GetDirectories(backups)
            .Where(directory => Stamp().IsMatch(Path.GetFileName(directory)))
            .OrderByDescending(directory => Path.GetFileName(directory), StringComparer.Ordinal)
            .Skip(Math.Max(1, options.Value.BackupCopies))];
        foreach (string directory in old)
            Directory.Delete(directory, recursive: true);
        return old.Length;
    }

    private static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (string file in Directory.GetFiles(source))
            File.Copy(file, Path.Combine(target, Path.GetFileName(file)));
        foreach (string directory in Directory.GetDirectories(source))
            CopyDirectory(directory, Path.Combine(target, Path.GetFileName(directory)));
    }

    [GeneratedRegex(@"^\d{8}-\d{6}$", RegexOptions.CultureInvariant)]
    private static partial Regex Stamp();

    [LoggerMessage(Level = LogLevel.Information, Message = "Backed up to {Folder}; {Deleted} older backups deleted")]
    private partial void LogBackedUp(string folder, int deleted);

    [LoggerMessage(Level = LogLevel.Warning, Message = "No Data Protection keys at {Keys} to back up")]
    private partial void LogNoKeys(string keys);
}
