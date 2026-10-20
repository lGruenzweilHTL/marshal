namespace Marshal.Memory.Idle;

/// <summary>
/// Memory work done while nobody talks to Marshal, after the nightly consolidation, for the owner of the current
/// execution context. Jobs read memory chunks only, never sessions or tool results, and change memory only by writing
/// proposals or project summaries; the user decides on proposals.
/// </summary>
public interface IIdleMemoryJob
{
    /// <summary>Short name, used in run triggers (<c>memory_idle:&lt;name&gt;</c>) and logs.</summary>
    string Name { get; }

    /// <summary>Runs the job over what changed since <paramref name="since"/>; returns how many items it wrote.</summary>
    Task<Abstractions.Result<int>> RunAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
