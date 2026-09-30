namespace Marshal.Tasks.Maintenance;

/// <summary>
/// The <c>maintenance</c> section of <c>marshal.yaml</c>: schedules (five-field cron, the owner's local time) and
/// limits of the seeded system tasks.
/// </summary>
public sealed class MaintenanceOptions
{
    public string BackupSchedule { get; set; } = "0 3 * * *";

    /// <summary>Backups kept in <c>/data/backups</c>; older ones are deleted after each backup.</summary>
    public int BackupCopies { get; set; } = 7;

    public string ConsolidationSchedule { get; set; } = "30 2 * * *";

    public string RetentionSchedule { get; set; } = "30 3 * * *";

    /// <summary>Full run step payloads (prompts, tool arguments and results, replies) are deleted after this.</summary>
    public TimeSpan PayloadRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Runs and their step metadata (tokens, cost, tools, durations) are deleted after this.</summary>
    public TimeSpan MetadataRetention { get; set; } = TimeSpan.FromDays(365);
}
