using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tokens;

namespace Marshal.Data.Tasks;

/// <summary>
/// One piece of long external work started by a task through an executor.
/// </summary>
public sealed class ExecutorSession : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required TaskId TaskId { get; init; }

    public required string Executor { get; init; }

    /// <summary>The executor's own id for the session.</summary>
    public required string Handle { get; set; }

    /// <summary>What the session works on (e.g. the repository), the target of its proposal.</summary>
    public string? Target { get; set; }

    /// <summary>The session's <c>executor_callback</c> token.</summary>
    public ApiTokenId? CallbackTokenId { get; set; }

    public ExecutorSessionStatus Status { get; set; } = ExecutorSessionStatus.Starting;

    public required DateTimeOffset StartedAt { get; init; }

    public DateTimeOffset? FinishedAt { get; set; }
}
