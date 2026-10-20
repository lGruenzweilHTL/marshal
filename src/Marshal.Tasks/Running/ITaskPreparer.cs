using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;

namespace Marshal.Tasks.Running;

/// <summary>
/// Runs a task's preparation run within <c>limits</c> (the task's remaining budget). The scheduler has already claimed
/// the preparation and stores the returned notes for the next main run.
/// </summary>
public interface ITaskPreparer
{
    Task<TaskPreparationOutcome> PrepareAsync(TaskRecord task, RunLimits limits, CancellationToken cancellationToken);
}

/// <summary>What a preparation run produced.</summary>
/// <param name="Body">The result body, or null when the run failed.</param>
/// <param name="Taint">Taint kinds the run collected.</param>
/// <param name="Spend">What it used (drawn from the task's budget).</param>
/// <param name="Reason">Why it failed, if it did.</param>
public sealed record TaskPreparationOutcome(string? Body, IReadOnlyList<string> Taint, TaskSpend Spend, string? Reason = null);
