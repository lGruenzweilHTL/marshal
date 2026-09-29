namespace Marshal.Abstractions.Printers;

/// <summary>
/// Webcams a printer host announces (Moonraker's <c>/server/webcams/list</c>).
/// </summary>
public interface IPrinterWebcams
{
    /// <summary>The printer's first enabled webcam with absolute URLs; <c>printer.no_webcam</c> when there is none.</summary>
    Task<Result<PrinterWebcam>> FindAsync(string printer, CancellationToken cancellationToken);

    /// <summary>Opens the webcam's live stream (usually MJPEG); <c>printer.no_webcam</c> when it has none.</summary>
    Task<Result<WebcamFeed>> OpenStreamAsync(string printer, CancellationToken cancellationToken);
}

/// <summary>An open webcam stream; dispose it to close the upstream connection.</summary>
public sealed class WebcamFeed(string contentType, Stream body, IDisposable? upstream = null) : IAsyncDisposable
{
    public string ContentType { get; } = contentType;

    public Stream Body { get; } = body;

    public async ValueTask DisposeAsync()
    {
        await Body.DisposeAsync();
        upstream?.Dispose();
    }
}

/// <summary>A printer webcam.</summary>
/// <param name="Name">Webcam name.</param>
/// <param name="SnapshotUrl">Single-image URL.</param>
/// <param name="StreamUrl">MJPEG stream URL, if any.</param>
public sealed record PrinterWebcam(string Name, Uri SnapshotUrl, Uri? StreamUrl);
