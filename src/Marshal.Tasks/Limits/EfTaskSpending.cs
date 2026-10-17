using Marshal.Abstractions.Execution;
using Marshal.Data;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.Limits;

/// <summary>
/// <see cref="ITaskSpending"/> from the <c>runs</c> table (owner-scoped): task and automation runs.
/// </summary>
public sealed class EfTaskSpending(IDbContextFactory<MarshalDbContext> contexts) : ITaskSpending
{
    public async Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<decimal> costs = await db.Runs
            .Where(run => (run.Source == SessionSource.Task || run.Source == SessionSource.Automation) && run.StartedAt >= since)
            .Select(run => run.Cost)
            .ToListAsync(cancellationToken);
        return costs.Sum();
    }
}
