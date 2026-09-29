using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Running;

/// <summary>
/// Executes one run of a task of its <see cref="Kind"/>. The scheduler has already claimed the task (status
/// <c>running</c>) and applies the returned outcome.
/// </summary>
public interface ITaskRunner
{
    TaskKind Kind { get; }

    Task<TaskRunOutcome> RunAsync(TaskRecord task, CancellationToken cancellationToken);
}
