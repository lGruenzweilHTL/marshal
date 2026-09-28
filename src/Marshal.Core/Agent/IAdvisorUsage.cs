namespace Marshal.Core.Agent;

/// <summary>
/// Advisor spending of the current owner, for the daily advisor cap.
/// </summary>
public interface IAdvisorUsage
{
    Task<decimal> GetAdvisorCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
