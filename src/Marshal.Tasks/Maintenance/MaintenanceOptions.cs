using Marshal.Abstractions.Maintenance;

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

    /// <summary>Skill proposals from repeated workflows, after consolidation (owners with "help Marshal learn" on).</summary>
    public string SkillProposalSchedule { get; set; } = "0 4 * * *";

    /// <summary>Full run step payloads (prompts, tool arguments and results, replies) are deleted after this.</summary>
    public TimeSpan PayloadRetention { get; set; } = TimeSpan.FromDays(30);

    /// <summary>Runs and their step metadata (tokens, cost, tools, durations) are deleted after this.</summary>
    public TimeSpan MetadataRetention { get; set; } = TimeSpan.FromDays(365);

    /// <summary>Usage-based maintenance items by id (<c>[a-z][a-z0-9_]*</c>); none by default.</summary>
    public Dictionary<string, MaintenanceItemOptions> Items { get; set; } = [];
}

/// <summary>One maintenance item: due every <see cref="Every"/> units of its counter.</summary>
public sealed class MaintenanceItemOptions
{
    /// <summary>Display name, e.g. "Voron nozzle".</summary>
    public string Name { get; set; } = "";

    public MaintenanceSourceOptions Source { get; set; } = new();

    /// <summary>The interval in the counter's unit.</summary>
    public decimal Every { get; set; }

    /// <summary>Notify this much before the interval is reached; less than <see cref="Every"/>.</summary>
    public decimal? WarnBefore { get; set; }
}

/// <summary>Exactly one of <see cref="Ha"/> or <see cref="Printer"/> (with <see cref="Metric"/>).</summary>
public sealed class MaintenanceSourceOptions
{
    /// <summary>A Home Assistant entity or statistic id with a numeric, increasing value.</summary>
    public string? Ha { get; set; }

    /// <summary>A configured printer's name.</summary>
    public string? Printer { get; set; }

    public MaintenanceMetric? Metric { get; set; }

    public MaintenanceSource ToSource() => new(Ha, Printer, Metric);
}
