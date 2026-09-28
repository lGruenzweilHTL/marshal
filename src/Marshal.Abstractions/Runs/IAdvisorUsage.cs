namespace Marshal.Abstractions.Runs;

/// <summary>
/// Advisor spending of the current owner, for the daily advisor cap.
/// </summary>
public interface IAdvisorUsage
{
    /// <summary>Sum of advisor-tier model call costs of runs started at or after <paramref name="since"/>.</summary>
    Task<decimal> GetAdvisorCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
