namespace Marshal.Tasks.Limits;

/// <summary>
/// What the current owner's background runs cost, from the recorded runs (same price table as the cost view).
/// </summary>
public interface ITaskSpending
{
    Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
