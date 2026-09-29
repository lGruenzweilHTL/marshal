namespace Marshal.Abstractions.Tasks;

/// <summary>
/// Limits of a task over all its runs. A null limit is not enforced at the task level (global limits still apply).
/// </summary>
public sealed record TaskBudget(long? Tokens = null, decimal? Cost = null, int? Steps = null, TimeSpan? WallTime = null, int? Runs = null);
