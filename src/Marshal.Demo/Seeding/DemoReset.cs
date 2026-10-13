using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Data.Demo;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Marshal.Demo.Seeding;

/// <summary>Removes the seeded demo data of an owner.</summary>
public interface IDemoReset
{
    /// <summary>Deletes the recorded seeded rows and their records; returns the number of rows deleted.</summary>
    Task<int> ResetAsync(OwnerId owner, CancellationToken cancellationToken);
}

/// <summary>
/// Deletes exactly the rows recorded in <c>demo_seed</c>, with the rows that only exist because of them (run steps,
/// chunk edges and versions, the chunks' index entries), in one transaction and in dependency order. A seeded row
/// that a row of the user still refers to (a seeded conversation the user continued) is kept, and its record is
/// dropped with the others, so it counts as the user's from then on.
/// </summary>
public sealed class DemoReset(IDbContextFactory<MarshalDbContext> contexts, IExecutionContextAccessor executionContext, IMemoryStore memory, IAuditLog audit) : IDemoReset
{
    public const string ResetAction = "demo.reset";

    public async Task<int> ResetAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        using IDisposable scope = executionContext.Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false });
        List<DemoSeed> seeds;
        await using (MarshalDbContext read = await contexts.CreateDbContextAsync(cancellationToken))
            seeds = await read.DemoSeeds.AsNoTracking().ToListAsync(cancellationToken);

        List<string> Ids(string entity) => [.. seeds.Where(seed => seed.Entity == entity).Select(seed => seed.RowId)];

        // The memory store owns the chunks' keyword and vector entries.
        List<ChunkId> chunks = [.. Ids("chunks").Select(ChunkId.From)];
        foreach (ChunkId chunk in chunks)
            await memory.DeleteAsync(chunk, cancellationToken);

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        await using IDbContextTransaction transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        int deleted = 0;

        await db.ChunkEdges.Where(edge => chunks.Contains(edge.FromId) || chunks.Contains(edge.ToId)).ExecuteDeleteAsync(cancellationToken);
        await db.ChunkVersions.Where(version => chunks.Contains(version.ChunkId)).ExecuteDeleteAsync(cancellationToken);
        foreach (ChunkId id in chunks)
            deleted += await TryDeleteAsync(db, transaction, () => db.Chunks.Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken), cancellationToken);

        foreach (PendingActionId id in Ids("pending_actions").Select(id => PendingActionId.Parse(id, null)))
            deleted += await TryDeleteAsync(db, transaction, () => db.PendingActions.Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken), cancellationToken);

        foreach (Guid id in Ids("notifications").Select(Guid.Parse))
            deleted += await TryDeleteAsync(db, transaction, () => db.Notifications.Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken), cancellationToken);

        foreach (RunId id in Ids("runs").Select(id => RunId.Parse(id, null)))
        {
            deleted += await TryDeleteAsync(db, transaction, async () =>
            {
                await db.RunSteps.Where(step => step.RunId == id).ExecuteDeleteAsync(cancellationToken);
                return await db.Runs.Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken);
            }, cancellationToken);
        }

        foreach (TaskId id in Ids("agent_tasks").Select(id => TaskId.Parse(id, null)))
            deleted += await TryDeleteAsync(db, transaction, () => db.AgentTasks.Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken), cancellationToken);

        foreach (SessionId id in Ids("sessions").Select(id => SessionId.Parse(id, null)))
            deleted += await TryDeleteAsync(db, transaction, () => db.Sessions.Where(row => row.Id == id).ExecuteDeleteAsync(cancellationToken), cancellationToken);

        await db.DemoSeeds.ExecuteDeleteAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        await audit.RecordAsync(new AuditEntry(AuditActorKind.System, "demo", ResetAction, Details: new { rows = deleted, recorded = seeds.Count }), cancellationToken);
        return deleted;
    }

    /// <summary>Runs one row's delete inside a savepoint; a row still referenced by a user row is kept (0).</summary>
    private static async Task<int> TryDeleteAsync(MarshalDbContext db, IDbContextTransaction transaction, Func<Task<int>> delete, CancellationToken cancellationToken)
    {
        const string Savepoint = "demo_row";
        await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
        try
        {
            int rows = await delete();
            await transaction.ReleaseSavepointAsync(Savepoint, cancellationToken);
            return rows;
        }
        catch (Microsoft.Data.Sqlite.SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            // SQLITE_CONSTRAINT: a foreign key from a row that is not seeded.
            await transaction.RollbackToSavepointAsync(Savepoint, cancellationToken);
            db.ChangeTracker.Clear();
            return 0;
        }
    }
}
