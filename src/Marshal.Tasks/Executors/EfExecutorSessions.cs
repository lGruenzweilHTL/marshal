using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Data.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.Executors;

/// <summary>
/// <see cref="IExecutorSessions"/> on the <c>executor_sessions</c> table, scoped to the current owner by the
/// context's query filter.
/// </summary>
internal sealed class EfExecutorSessions(IDbContextFactory<MarshalDbContext> contexts) : IExecutorSessions
{
    private const int MaxSessions = 100;

    public async Task<IReadOnlyList<ExecutorSessionRecord>> ListAsync(TaskId? task, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        IQueryable<ExecutorSession> rows = db.ExecutorSessions.AsNoTracking();
        if (task is { } id)
            rows = rows.Where(row => row.TaskId == id);

        return await rows.OrderByDescending(row => row.StartedAt)
            .Take(MaxSessions)
            .Select(row => new ExecutorSessionRecord(row.Id, row.TaskId, row.Executor, row.Target, row.Status, row.StartedAt, row.FinishedAt))
            .ToListAsync(cancellationToken);
    }
}
