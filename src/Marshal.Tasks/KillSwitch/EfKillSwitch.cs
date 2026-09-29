using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Data.Identity;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.KillSwitch;

/// <summary>
/// <see cref="IKillSwitch"/> kept in the owner's preferences row (<c>tasks_paused_at</c>), so a pause survives restarts.
/// </summary>
public sealed class EfKillSwitch(
    IDbContextFactory<MarshalDbContext> contexts,
    ITaskStore store,
    IUserPreferencesStore preferences,
    ITaskScheduler scheduler,
    IAuditLog audit,
    IExecutionContextAccessor executionContext,
    TimeProvider time) : IKillSwitch
{
    /// <summary>Status reason of tasks that became due while the switch was active.</summary>
    public const string PausedReason = "paused";

    private const int UpdateAttempts = 3;

    private static readonly AgentTaskStatus[] _unfinished = [AgentTaskStatus.Pending, AgentTaskStatus.Running, AgentTaskStatus.Waiting];

    public Task<bool> IsActiveAsync(CancellationToken cancellationToken) => new TaskPauses(contexts).IsPausedAsync(Context.OwnerId, cancellationToken);

    public async Task<int> ActivateAsync(CancellationToken cancellationToken)
    {
        await SetPausedAsync(time.GetUtcNow(), cancellationToken);
        int cancelled = 0;
        foreach (TaskRecord task in await store.ListAsync(new TaskFilter(_unfinished), cancellationToken))
        {
            if (task.Kind != TaskKind.Agent)
                continue;
            if (await CancelAsync(task.Id, cancellationToken))
                cancelled++;
            scheduler.CancelRun(task.Id);
        }

        await AuditAsync("tasks.stop_all", new { cancelled }, cancellationToken);
        return cancelled;
    }

    public async Task ReleaseAsync(CancellationToken cancellationToken)
    {
        await SetPausedAsync(null, cancellationToken);
        foreach (TaskRecord task in await store.ListAsync(new TaskFilter([AgentTaskStatus.Pending, AgentTaskStatus.Waiting]), cancellationToken))
        {
            if (task.StatusReason == PausedReason)
                await store.UpdateAsync(task with { StatusReason = null, NextRunAt = time.GetUtcNow() }, cancellationToken);
        }

        await AuditAsync("tasks.resume", null, cancellationToken);
        scheduler.Wake();
    }

    private MarshalExecutionContext Context =>
        executionContext.Current ?? throw new InvalidOperationException("Kill switch used without an execution context.");

    private async Task<bool> CancelAsync(TaskId id, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < UpdateAttempts; attempt++)
        {
            Result<TaskRecord> task = await store.GetAsync(id, cancellationToken);
            if (task.IsFailure || task.Value.Status is not (AgentTaskStatus.Pending or AgentTaskStatus.Running or AgentTaskStatus.Waiting))
                return false;
            if ((await store.UpdateAsync(task.Value with { Status = AgentTaskStatus.Cancelled, StatusReason = "stop_all", NextRunAt = null }, cancellationToken)).IsSuccess)
                return true;
        }

        return false;
    }

    private async Task SetPausedAsync(DateTimeOffset? pausedAt, CancellationToken cancellationToken)
    {
        OwnerId owner = Context.OwnerId;
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        UserPreferences? row = await db.UserPreferences.SingleOrDefaultAsync(candidate => candidate.UserId == owner, cancellationToken);
        if (row is null)
        {
            // Store the defaults first, so the row exists with the same values the preferences view shows.
            await preferences.SaveAsync(owner, await preferences.GetAsync(owner, cancellationToken), cancellationToken);
            row = await db.UserPreferences.SingleAsync(candidate => candidate.UserId == owner, cancellationToken);
        }

        row.TasksPausedAt = pausedAt;
        await db.SaveChangesAsync(cancellationToken);
    }

    private Task AuditAsync(string action, object? details, CancellationToken cancellationToken) =>
        audit.RecordAsync(new AuditEntry(AuditActorKind.User, Context.OwnerId.ToString(), action, Details: details, RunId: Context.RunId), cancellationToken);
}
