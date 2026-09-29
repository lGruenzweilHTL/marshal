using Marshal.Abstractions;
using Marshal.Abstractions.Printers;

namespace Marshal.Integrations.Moonraker.Tests;

/// <summary>Records commands; answers reads from settable values.</summary>
internal sealed class FakePrinterClient : IPrinterClient
{
    public List<(string Printer, PrinterCommand Command)> Sent { get; } = [];

    public IReadOnlyList<PrinterFile> Files { get; set; } = [];

    public JobQueueStatus Queue { get; set; } = new("ready", []);

    public IReadOnlyList<PrintJob> History { get; set; } = [];

    public Error? Failure { get; set; }

    public Task<Result<IReadOnlyList<PrinterFile>>> ListFilesAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } error ? Result.Failure<IReadOnlyList<PrinterFile>>(error) : Result.Success(Files));

    public Task<Result<JobQueueStatus>> GetQueueAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } error ? Result.Failure<JobQueueStatus>(error) : Result.Success(Queue));

    public Task<Result<IReadOnlyList<PrintJob>>> GetHistoryAsync(string printer, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } error ? Result.Failure<IReadOnlyList<PrintJob>>(error) : Result.Success<IReadOnlyList<PrintJob>>([.. History.Take(limit)]));

    public Task<Result> SendAsync(string printer, PrinterCommand command, CancellationToken cancellationToken)
    {
        if (Failure is { } error)
            return Task.FromResult(Result.Failure(error));

        Sent.Add((printer, command));
        return Task.FromResult(Result.Success());
    }
}

/// <summary>Printer states set by the test; unknown printers read as offline.</summary>
internal sealed class FakePrinterStateCache : IPrinterStateCache
{
    public Dictionary<string, PrinterState> States { get; } = [];

    public Result<PrinterState> Get(string printer) =>
        States.TryGetValue(printer, out PrinterState? state) ? state : PrinterErrors.Offline(printer);

    public IDisposable Subscribe(Action<PrinterState> changed) => new Nothing();

    private sealed class Nothing : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

/// <summary>Webcams per printer; printers without one answer <c>printer.no_webcam</c>.</summary>
internal sealed class FakeWebcams : IPrinterWebcams
{
    public Dictionary<string, PrinterWebcam> Webcams { get; } = [];

    public Task<Result<PrinterWebcam>> FindAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult(Webcams.TryGetValue(printer, out PrinterWebcam? webcam) ? Result.Success(webcam) : Result.Failure<PrinterWebcam>(PrinterErrors.NoWebcam(printer)));
}

/// <summary>Returns a fixed snapshot for every capture.</summary>
internal sealed class FakeSnapshotSource : ISnapshotSource
{
    public byte[] Jpeg { get; set; } = [0xFF, 0xD8, 0xFF, 0xE0];

    public int Captures { get; private set; }

    public Task<Result<PrinterSnapshot>> CaptureAsync(string printer, CancellationToken cancellationToken)
    {
        Captures++;
        return Task.FromResult(Result.Success(new PrinterSnapshot(printer, "snap1", $"/api/printers/{printer}/snapshots/snap1", Jpeg, 1024, 768, DateTimeOffset.UnixEpoch)));
    }

    public Task<Result<byte[]>> OpenAsync(string printer, string id, CancellationToken cancellationToken) =>
        Task.FromResult(Result.Success(Jpeg));
}
