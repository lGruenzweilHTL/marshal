using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Runs;

/// <summary>
/// The global daily cost cap shared by background tasks, AI tasks, eval replays and idle-time memory jobs.
/// </summary>
public interface IDailyCostCap
{
    /// <summary>What is left of the cap for the owner's current local day (zero or less when it is used up).</summary>
    Task<decimal> LeftTodayAsync(OwnerId owner, CancellationToken cancellationToken);
}
