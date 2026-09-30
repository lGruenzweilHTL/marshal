namespace Marshal.Abstractions.Printers;

/// <summary>
/// Read-only state of the print babysitter, for <c>printer.status</c> and the printer page. Nothing can start, stop
/// or reconfigure the babysitter through this interface.
/// </summary>
public interface IPrintBabysitterStatus
{
    /// <summary>The babysitter's state for a printer; null when it is not enabled for that printer.</summary>
    BabysitterStatus? Get(string printer);
}

/// <summary>Babysitter state of one printer.</summary>
/// <param name="Active">True while it watches a print.</param>
/// <param name="Verdicts">The latest checks, oldest first.</param>
/// <param name="PausedAt">When it last paused a print.</param>
public sealed record BabysitterStatus(bool Active, IReadOnlyList<BabysitterCheck> Verdicts, DateTimeOffset? PausedAt);

/// <summary>One check of a snapshot.</summary>
/// <param name="At">When the snapshot was judged.</param>
/// <param name="Outcome"><c>ok</c>, <c>failure</c> or <c>unknown</c> (no snapshot or no verdict).</param>
/// <param name="Reason">What the detector saw, or why there is no verdict.</param>
/// <param name="SnapshotUrl">Same-origin URL of the snapshot.</param>
public sealed record BabysitterCheck(DateTimeOffset At, string Outcome, string Reason, string? SnapshotUrl);
