using Marshal.Abstractions.Execution;
using Marshal.Data;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.Limits;

/// <summary>
/// <see cref="ITaskSpending"/> from the <c>runs</c> table (owner-scoped): task and automation runs, and the runs of
/// Marshal's MCP server (source <c>api</c>, trigger <c>mcp:*</c>), plus eval replays (<c>eval_results</c>).
/// </summary>
public sealed class EfTaskSpending(IDbContextFactory<MarshalDbContext> contexts) : ITaskSpending
{
    private const string McpTriggerPrefix = "mcp:";

    public async Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        List<decimal> costs = await db.Runs
            .Where(run => (run.Source == SessionSource.Task || run.Source == SessionSource.Automation || (run.Source == SessionSource.Api && run.Trigger.StartsWith(McpTriggerPrefix))) && run.StartedAt >= since)
            .Select(run => run.Cost)
            .ToListAsync(cancellationToken);
        // Eval replays draw from the same cap.
        List<decimal> evals = await db.EvalResults.Where(result => result.CreatedAt >= since).Select(result => result.Cost).ToListAsync(cancellationToken);
        return costs.Sum() + evals.Sum();
    }
}
