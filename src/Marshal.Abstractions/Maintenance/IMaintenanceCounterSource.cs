namespace Marshal.Abstractions.Maintenance;

/// <summary>
/// Reads the counter behind a maintenance item (print hours, cycles, consumption): a numeric value that only grows
/// until it is reset.
/// </summary>
public interface IMaintenanceCounterSource
{
    /// <summary>Whether this source reads <paramref name="source"/> (an HA entity or statistic, or a printer metric).</summary>
    bool Handles(MaintenanceSource source);

    /// <summary>The counter's current value; fails while the integration is unavailable or the value is not a number.</summary>
    Task<Result<MaintenanceReading>> ReadAsync(MaintenanceSource source, CancellationToken cancellationToken);

    /// <summary>Counters that could back a maintenance item (for suggesting items); none by default.</summary>
    Task<IReadOnlyList<MaintenanceCounter>> ListCountersAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<MaintenanceCounter>>([]);
}

/// <summary>A counter that could back a maintenance item.</summary>
/// <param name="Source">Where it is read.</param>
/// <param name="Name">Its display name (a printer name or the entity's friendly name).</param>
/// <param name="Reading">Its current value, when known.</param>
public sealed record MaintenanceCounter(MaintenanceSource Source, string Name, MaintenanceReading? Reading);

/// <summary>Where a maintenance item's counter comes from: exactly one of <paramref name="Ha"/> or <paramref name="Printer"/>.</summary>
/// <param name="Ha">A Home Assistant entity id (its numeric state) or statistic id (its <c>sum</c>).</param>
/// <param name="Printer">A configured printer's name.</param>
/// <param name="Metric">With <paramref name="Printer"/>: which of Moonraker's job totals.</param>
public sealed record MaintenanceSource(string? Ha = null, string? Printer = null, MaintenanceMetric? Metric = null);

/// <summary>A printer's usage counter from Moonraker's job totals.</summary>
public enum MaintenanceMetric
{
    /// <summary><c>total_print_time</c> in hours.</summary>
    PrintHours,

    /// <summary><c>total_filament_used</c> in meters.</summary>
    FilamentMeters,
}

/// <summary>A counter value and its unit (<c>h</c>, <c>m</c>, or the HA unit of measurement; null when unknown).</summary>
public sealed record MaintenanceReading(decimal Value, string? Unit);
