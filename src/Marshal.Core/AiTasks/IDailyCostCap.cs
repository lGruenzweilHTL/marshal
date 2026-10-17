using Marshal.Abstractions.Identity;

namespace Marshal.Core.AiTasks;

/// <summary>
/// The global daily cost cap shared by background tasks and AI tasks.
/// </summary>
public interface IDailyCostCap
{
    /// <summary>What is left of the cap for the owner's current local day (zero or less when it is used up).</summary>
    Task<decimal> LeftTodayAsync(OwnerId owner, CancellationToken cancellationToken);
}
