namespace Marshal.Abstractions.Printers;

/// <summary>
/// Reads and commands for one printer host. Commands are typed; there is no way to send free-form G-code.
/// </summary>
public interface IPrinterClient
{
    /// <summary>Printable files (the <c>gcodes</c> root), newest first.</summary>
    Task<Result<IReadOnlyList<PrinterFile>>> ListFilesAsync(string printer, CancellationToken cancellationToken);

    /// <summary>The job queue.</summary>
    Task<Result<JobQueueStatus>> GetQueueAsync(string printer, CancellationToken cancellationToken);

    /// <summary>The newest <paramref name="limit"/> finished or running jobs, newest first.</summary>
    Task<Result<IReadOnlyList<PrintJob>>> GetHistoryAsync(string printer, int limit, CancellationToken cancellationToken);

    /// <summary>Validates and sends a command; <c>printer.invalid_command</c> or <c>printer.macro_not_allowed</c> before anything is sent.</summary>
    Task<Result> SendAsync(string printer, PrinterCommand command, CancellationToken cancellationToken);
}

/// <summary>A printable file.</summary>
/// <param name="Path">Path relative to the <c>gcodes</c> root.</param>
/// <param name="Modified">Last change.</param>
/// <param name="Size">Size in bytes.</param>
public sealed record PrinterFile(string Path, DateTimeOffset Modified, long Size);

/// <summary>The job queue.</summary>
/// <param name="State">Queue state: <c>ready</c>, <c>loading</c>, <c>starting</c> or <c>paused</c>.</param>
/// <param name="Jobs">Queued jobs in order.</param>
public sealed record JobQueueStatus(string State, IReadOnlyList<QueuedJob> Jobs);

/// <summary>A queued job.</summary>
public sealed record QueuedJob(string JobId, string Filename, DateTimeOffset AddedAt);

/// <summary>A job from the print history.</summary>
/// <param name="JobId">Moonraker job id.</param>
/// <param name="Filename">Printed file.</param>
/// <param name="Status">e.g. <c>completed</c>, <c>cancelled</c>, <c>error</c>, <c>in_progress</c>.</param>
/// <param name="StartedAt">Start time.</param>
/// <param name="EndedAt">End time, when finished.</param>
/// <param name="PrintDuration">Seconds spent printing.</param>
/// <param name="TotalDuration">Seconds including pauses.</param>
/// <param name="FilamentUsed">Filament in mm.</param>
/// <param name="Exists">Whether the file still exists.</param>
public sealed record PrintJob(string JobId, string Filename, string Status, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, double PrintDuration, double TotalDuration, double FilamentUsed, bool Exists);
