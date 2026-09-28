namespace Marshal.Abstractions.Runs;

/// <summary>
/// Reads recorded runs of the current owner for the runs and cost views.
/// </summary>
public interface IRunQueries
{
    /// <summary>Runs matching <paramref name="filter"/>, newest first.</summary>
    Task<IReadOnlyList<RunSummary>> ListAsync(RunFilter filter, CancellationToken cancellationToken);

    /// <summary>One run with its steps; null when it does not exist for this owner.</summary>
    Task<RunDetail?> GetAsync(RunId id, CancellationToken cancellationToken);

    /// <summary>
    /// Cost per local day and model tier from the recorded model calls (the numbers budgets use), advisor separately.
    /// </summary>
    Task<CostReport> CostAsync(DateOnly from, DateOnly to, TimeZoneInfo timeZone, CancellationToken cancellationToken);
}
