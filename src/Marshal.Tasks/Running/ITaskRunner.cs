using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;

namespace Marshal.Tasks.Running;

/// <summary>
/// Executes one run of a task of its <see cref="Kind"/> within <c>limits</c> (the task's remaining budget). The
/// scheduler has already claimed the task (status <c>running</c>) and applies the returned outcome and spend.
/// </summary>
public interface ITaskRunner
{
    TaskKind Kind { get; }

    Task<TaskRunOutcome> RunAsync(TaskRecord task, RunLimits limits, CancellationToken cancellationToken);
}
