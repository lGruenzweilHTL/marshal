using Marshal.Abstractions;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Printers;

namespace Marshal.Demo.Printers;

/// <summary>The demo printer's job totals as maintenance counters, in place of Moonraker's <c>/server/history/totals</c>.</summary>
public sealed class DemoPrinterCounterSource(TimeProvider time) : IMaintenanceCounterSource
{
    public bool Handles(MaintenanceSource source) => source.Printer is not null;

    public Task<Result<MaintenanceReading>> ReadAsync(MaintenanceSource source, CancellationToken cancellationToken)
    {
        if (!string.Equals(source.Printer, DemoPrinter.Name, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Result<MaintenanceReading>>(PrinterErrors.NotFound(source.Printer ?? ""));

        DateTimeOffset now = time.GetUtcNow();
        return Task.FromResult<Result<MaintenanceReading>>(source.Metric == MaintenanceMetric.FilamentMeters
            ? new MaintenanceReading(DemoPrinter.TotalFilamentMeters(now), "m")
            : new MaintenanceReading(DemoPrinter.TotalPrintHours(now), "h"));
    }

    public Task<IReadOnlyList<MaintenanceCounter>> ListCountersAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<MaintenanceCounter>>(
        [
            new MaintenanceCounter(new MaintenanceSource(Printer: DemoPrinter.Name, Metric: MaintenanceMetric.PrintHours), DemoPrinter.Name,
                new MaintenanceReading(DemoPrinter.TotalPrintHours(time.GetUtcNow()), "h")),
        ]);
}
