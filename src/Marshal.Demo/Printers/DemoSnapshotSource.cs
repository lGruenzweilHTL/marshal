using System.Collections.Concurrent;
using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;

namespace Marshal.Demo.Printers;

/// <summary>Snapshots of the simulated webcam, kept in memory (the newest 20) for the snapshot links.</summary>
public sealed class DemoSnapshotSource(DemoWorld world, ISnapshotLinks links) : ISnapshotSource
{
    private const int Kept = 20;

    private readonly ConcurrentDictionary<string, byte[]> _snapshots = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _order = new();

    public Task<Result<PrinterSnapshot>> CaptureAsync(string printer, CancellationToken cancellationToken)
    {
        if (!string.Equals(printer, DemoPrinter.Name, StringComparison.OrdinalIgnoreCase))
            return Task.FromResult<Result<PrinterSnapshot>>(PrinterErrors.NotFound(printer));

        PrinterState state = world.Printer.State();
        DateTimeOffset now = world.Time.GetUtcNow();
        string id = now.UtcDateTime.ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture) + "-" + Guid.NewGuid().ToString("N")[..6];
        byte[] jpeg = DemoPrinterImage.Jpeg(state.Progress, state.PrintState);
        _snapshots[id] = jpeg;
        _order.Enqueue(id);
        while (_order.Count > Kept && _order.TryDequeue(out string? old))
            _snapshots.TryRemove(old, out _);

        return Task.FromResult<Result<PrinterSnapshot>>(new PrinterSnapshot(DemoPrinter.Name, id, links.For(DemoPrinter.Name, id), jpeg, DemoPrinterImage.Width, DemoPrinterImage.Height, now));
    }

    public Task<Result<byte[]>> OpenAsync(string printer, string id, CancellationToken cancellationToken) =>
        Task.FromResult<Result<byte[]>>(_snapshots.TryGetValue(id, out byte[]? jpeg) ? jpeg : PrinterErrors.SnapshotNotFound(id));
}
