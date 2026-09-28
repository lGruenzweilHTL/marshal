using Marshal.Abstractions.Runs;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Data.Conversations;

/// <summary>
/// Advisor spending of the current owner from the recorded model calls.
/// </summary>
public sealed class EfAdvisorUsage(IDbContextFactory<MarshalDbContext> contexts) : IAdvisorUsage
{
    public async Task<decimal> GetAdvisorCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<decimal?> costs = await db.RunSteps
            .Where(step => step.Kind == RunStepKind.ModelCall && step.Tier == "advisor")
            .Join(db.Runs.Where(run => run.StartedAt >= since), step => step.RunId, run => run.Id, (step, _) => step.Cost)
            .ToListAsync(cancellationToken);
        return costs.Sum(cost => cost ?? 0m);
    }
}
