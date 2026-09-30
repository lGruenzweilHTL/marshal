namespace Marshal.Abstractions.Printers;

/// <summary>
/// Judges whether a webcam snapshot shows a failed print (spaghetti, a part off the bed, a blob on the nozzle).
/// </summary>
public interface IPrintFailureDetector
{
    /// <summary>The verdict; a failure result when the snapshot could not be judged (it never counts as positive).</summary>
    Task<Result<PrintVerdict>> DetectAsync(PrinterSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>A detector verdict.</summary>
/// <param name="Failure">True when the print looks failed.</param>
/// <param name="Reason">Short description of what was seen.</param>
public sealed record PrintVerdict(bool Failure, string Reason);
