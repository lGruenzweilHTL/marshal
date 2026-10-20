namespace Marshal.Abstractions.Tasks;

/// <summary>
/// Durable tasks. Every method except the due queries is scoped to the current owner.
/// </summary>
public interface ITaskStore
{
    /// <summary>Validates and stores a task for the current owner; computes its first run time.</summary>
    Task<Result<TaskRecord>> CreateAsync(NewTask task, CancellationToken cancellationToken);

    Task<Result<TaskRecord>> GetAsync(TaskId id, CancellationToken cancellationToken);

    /// <summary>Finds a task by id or by name.</summary>
    Task<Result<TaskRecord>> FindAsync(string reference, CancellationToken cancellationToken);

    Task<IReadOnlyList<TaskRecord>> ListAsync(TaskFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Saves the changeable fields of <paramref name="task"/> (goal, template, trigger and payload, status, next run,
    /// preparation and prepared notes, notes, spend, name, deliveries, quiet-hours override, result). Owner, parent, kind, budget, project, origin
    /// satellite and depth never change. Fails with <c>tasks.conflict</c> when the row version is stale.
    /// </summary>
    Task<Result<TaskRecord>> UpdateAsync(TaskRecord task, CancellationToken cancellationToken);

    /// <summary>Replaces the task's tool policy (a user action in the web UI; the caller validates and audits it).</summary>
    Task<Result<TaskRecord>> SetToolPolicyAsync(TaskId id, IReadOnlyList<string> policy, CancellationToken cancellationToken);

    /// <summary>Pending or waiting tasks of every owner with <c>next_run_at</c> at or before <paramref name="now"/>, earliest first.</summary>
    Task<IReadOnlyList<OwnedTask>> GetDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>Pending or waiting tasks of every owner with <c>next_prepare_at</c> at or before <paramref name="now"/>, earliest first.</summary>
    Task<IReadOnlyList<OwnedTask>> GetDuePreparationsAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken);

    /// <summary>The earliest <c>next_run_at</c> or <c>next_prepare_at</c> of any pending or waiting task of every owner.</summary>
    Task<DateTimeOffset?> GetNextRunAtAsync(CancellationToken cancellationToken);

    /// <summary>Armed <c>event</c> tasks of every owner: pending or waiting, not already due.</summary>
    Task<IReadOnlyList<TaskWatch>> GetWatchesAsync(CancellationToken cancellationToken);

    /// <summary>Tasks of every owner with a result waiting for its delivery conditions.</summary>
    Task<IReadOnlyList<OwnedTask>> GetPendingDeliveriesAsync(CancellationToken cancellationToken);

    /// <summary>Tasks of every owner left <c>running</c> (by a crash when called at startup).</summary>
    Task<IReadOnlyList<OwnedTask>> GetRunningAsync(CancellationToken cancellationToken);
}
