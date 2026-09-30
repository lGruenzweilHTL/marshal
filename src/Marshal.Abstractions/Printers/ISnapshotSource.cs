namespace Marshal.Abstractions.Printers;

/// <summary>
/// Captures, stores and serves resized webcam snapshots.
/// </summary>
public interface ISnapshotSource
{
    /// <summary>Fetches a snapshot from the printer's webcam, resizes it to JPEG and stores it.</summary>
    Task<Result<PrinterSnapshot>> CaptureAsync(string printer, CancellationToken cancellationToken);

    /// <summary>A stored snapshot's JPEG bytes; <c>printer.snapshot_not_found</c> when it is gone.</summary>
    Task<Result<byte[]>> OpenAsync(string printer, string id, CancellationToken cancellationToken);
}

/// <summary>A stored snapshot.</summary>
/// <param name="Printer">Printer name.</param>
/// <param name="Id">Snapshot id (file name without extension).</param>
/// <param name="Url">Same-origin URL that serves it to signed-in users.</param>
/// <param name="Jpeg">The resized JPEG.</param>
/// <param name="Width">Width in pixels.</param>
/// <param name="Height">Height in pixels.</param>
/// <param name="CapturedAt">Capture time.</param>
public sealed record PrinterSnapshot(string Printer, string Id, string Url, byte[] Jpeg, int Width, int Height, DateTimeOffset CapturedAt);

/// <summary>
/// Builds the same-origin URL of a stored snapshot (the host knows its path base and API version).
/// </summary>
public interface ISnapshotLinks
{
    /// <summary>URL for signed-in users.</summary>
    string For(string printer, string id);

    /// <summary>
    /// URL carrying a signature that expires after a day, readable without signing in: for a push notification's
    /// image, which the phone fetches without Marshal's cookie.
    /// </summary>
    string Shared(string printer, string id);
}
