namespace Marshal.Abstractions.Tasks;

/// <summary>
/// Code-only work behind a <c>system</c> task (no agent, no tokens): backups, consolidation, retention. The task's
/// <c>job</c> names it. Runs as the task's owner; jobs that cover every owner read across owners themselves.
/// </summary>
public interface ISystemJob
{
    /// <summary>The job name stored on the task, e.g. <c>backup</c>.</summary>
    string Name { get; }

    /// <summary>Runs once; the text says what was done (the task's result).</summary>
    Task<Result<string>> RunAsync(CancellationToken cancellationToken);
}
