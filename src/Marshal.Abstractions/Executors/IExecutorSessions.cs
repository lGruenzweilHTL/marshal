using Marshal.Abstractions.Tasks;

namespace Marshal.Abstractions.Executors;

/// <summary>
/// The current owner's executor sessions, for the tasks view.
/// </summary>
public interface IExecutorSessions
{
    /// <summary>Sessions of <paramref name="task"/> (all when null), newest first.</summary>
    Task<IReadOnlyList<ExecutorSessionRecord>> ListAsync(TaskId? task, CancellationToken cancellationToken);
}

/// <summary>An executor session.</summary>
public sealed record ExecutorSessionRecord(
    Guid Id,
    TaskId TaskId,
    string Executor,
    string? Target,
    ExecutorSessionStatus Status,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt);
