using Marshal.Abstractions;
using Marshal.Abstractions.Printers;

namespace Marshal.Testing;

/// <summary>Records commands; answers reads from settable values.</summary>
public sealed class FakePrinterClient : IPrinterClient
{
    public List<(string Printer, PrinterCommand Command)> Sent { get; } = [];

    public IReadOnlyList<PrinterFile> Files { get; set; } = [];

    public JobQueueStatus Queue { get; set; } = new("ready", []);

    public IReadOnlyList<PrintJob> History { get; set; } = [];

    /// <summary>Failure every read and send returns.</summary>
    public Error? Failure { get; set; }

    /// <summary>Validation result per command (success when not set).</summary>
    public Func<PrinterCommand, Result> Validation { get; set; } = _ => Result.Success();

    public Task<Result<IReadOnlyList<PrinterFile>>> ListFilesAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } error ? Result.Failure<IReadOnlyList<PrinterFile>>(error) : Result.Success(Files));

    public Task<Result<JobQueueStatus>> GetQueueAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } error ? Result.Failure<JobQueueStatus>(error) : Result.Success(Queue));

    public Task<Result<IReadOnlyList<PrintJob>>> GetHistoryAsync(string printer, int limit, CancellationToken cancellationToken) =>
        Task.FromResult(Failure is { } error ? Result.Failure<IReadOnlyList<PrintJob>>(error) : Result.Success<IReadOnlyList<PrintJob>>([.. History.Take(limit)]));

    public Result Validate(string printer, PrinterCommand command) => Validation(command);

    public Task<Result> SendAsync(string printer, PrinterCommand command, CancellationToken cancellationToken)
    {
        Result valid = Validation(command);
        if (valid.IsFailure)
            return Task.FromResult(valid);
        if (Failure is { } error)
            return Task.FromResult(Result.Failure(error));

        lock (Sent)
            Sent.Add((printer, command));
        return Task.FromResult(Result.Success());
    }
}

/// <summary>Printer states set by the test; unknown printers read as offline. <see cref="Set"/> notifies subscribers.</summary>
public sealed class FakePrinterStateCache : IPrinterStateCache
{
    private readonly List<Action<string, PrinterState?>> _subscribers = [];

    public Dictionary<string, PrinterState> States { get; } = [];

    public Result<PrinterState> Get(string printer) =>
        States.TryGetValue(printer, out PrinterState? state) ? state : PrinterErrors.Offline(printer);

    /// <summary>Sets (or with null removes) a printer's state and notifies subscribers.</summary>
    public void Set(string printer, PrinterState? state)
    {
        if (state is null)
            States.Remove(printer);
        else
            States[printer] = state;

        Action<string, PrinterState?>[] subscribers;
        lock (_subscribers)
            subscribers = [.. _subscribers];
        foreach (Action<string, PrinterState?> subscriber in subscribers)
            subscriber(printer, state);
    }

    public IDisposable Subscribe(Action<string, PrinterState?> changed)
    {
        lock (_subscribers)
            _subscribers.Add(changed);
        return new Subscription(() =>
        {
            lock (_subscribers)
                _subscribers.Remove(changed);
        });
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}

/// <summary>Webcams per printer; printers without one answer <c>printer.no_webcam</c>.</summary>
public sealed class FakeWebcams : IPrinterWebcams
{
    public Dictionary<string, PrinterWebcam> Webcams { get; } = [];

    /// <summary>Stream content type and bytes served by <see cref="OpenStreamAsync"/>.</summary>
    public (string ContentType, byte[] Body) Stream { get; set; } = ("multipart/x-mixed-replace;boundary=frame", "--frame\r\n"u8.ToArray());

    public Task<Result<PrinterWebcam>> FindAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult(Webcams.TryGetValue(printer, out PrinterWebcam? webcam) ? Result.Success(webcam) : Result.Failure<PrinterWebcam>(PrinterErrors.NoWebcam(printer)));

    public Task<Result<WebcamFeed>> OpenStreamAsync(string printer, CancellationToken cancellationToken) =>
        Task.FromResult(Webcams.ContainsKey(printer)
            ? Result.Success(new WebcamFeed(Stream.ContentType, new MemoryStream(Stream.Body)))
            : Result.Failure<WebcamFeed>(PrinterErrors.NoWebcam(printer)));
}

/// <summary>Returns a fixed snapshot for every capture and serves it back by id.</summary>
public sealed class FakeSnapshotSource : ISnapshotSource
{
    public byte[] Jpeg { get; set; } = [0xFF, 0xD8, 0xFF, 0xE0];

    public int Captures { get; private set; }

    /// <summary>Builds snapshot links (e.g. the host's); a fixed test path when not set.</summary>
    public ISnapshotLinks? Links { get; set; }

    public Task<Result<PrinterSnapshot>> CaptureAsync(string printer, CancellationToken cancellationToken)
    {
        Captures++;
        string url = Links?.For(printer, "snap1") ?? $"/api/printers/{printer}/snapshots/snap1";
        return Task.FromResult(Result.Success(new PrinterSnapshot(printer, "snap1", url, Jpeg, 1024, 768, DateTimeOffset.UnixEpoch)));
    }

    public Task<Result<byte[]>> OpenAsync(string printer, string id, CancellationToken cancellationToken) =>
        Task.FromResult(id == "snap1" && Captures > 0 ? Result.Success(Jpeg) : Result.Failure<byte[]>(PrinterErrors.SnapshotNotFound(id)));
}
