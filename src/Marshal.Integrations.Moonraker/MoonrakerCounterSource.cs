using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Moonraker;

/// <summary>
/// Printer counters from Moonraker's job totals (<c>GET /server/history/totals</c>): <c>total_print_time</c> in seconds
/// as hours, <c>total_filament_used</c> in millimeters as meters. Lists each configured printer's print hours as a
/// candidate counter.
/// </summary>
public sealed class MoonrakerCounterSource(IOptions<PrinterOptions> options, IHttpClientFactory httpClients) : IMaintenanceCounterSource
{
    private readonly MoonrakerRest _rest = new(options, httpClients);

    public bool Handles(MaintenanceSource source) => source.Printer is not null;

    public async Task<Result<MaintenanceReading>> ReadAsync(MaintenanceSource source, CancellationToken cancellationToken)
    {
        if (source.Printer is not { } printer)
            return PrinterErrors.NotFound("");

        Result<JsonElement> totals = await _rest.RequestAsync(printer, HttpMethod.Get, "server/history/totals", null, cancellationToken);
        if (totals.IsFailure)
            return totals.Error;

        string field = source.Metric == MaintenanceMetric.FilamentMeters ? "total_filament_used" : "total_print_time";
        if (!totals.Value.TryGetProperty("job_totals", out JsonElement jobs)
            || !jobs.TryGetProperty(field, out JsonElement value)
            || value.ValueKind != JsonValueKind.Number)
        {
            return PrinterErrors.CommandFailed(printer, $"Moonraker's job totals have no {field}.");
        }

        decimal raw = value.GetDecimal();
        return source.Metric == MaintenanceMetric.FilamentMeters
            ? new MaintenanceReading(raw / 1000m, "m")
            : new MaintenanceReading(raw / 3600m, "h");
    }

    public async Task<IReadOnlyList<MaintenanceCounter>> ListCountersAsync(CancellationToken cancellationToken)
    {
        var counters = new List<MaintenanceCounter>();
        foreach (PrinterConfig printer in options.Value.Printers)
        {
            var source = new MaintenanceSource(Printer: printer.Name, Metric: MaintenanceMetric.PrintHours);
            Result<MaintenanceReading> reading = await ReadAsync(source, cancellationToken);
            counters.Add(new MaintenanceCounter(source, printer.Name, reading.IsSuccess ? reading.Value : null));
        }

        return counters;
    }
}
