using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Running;

/// <summary>
/// How a task run ended.
/// </summary>
public sealed record TaskRunOutcome
{
    public required TaskRunEnd End { get; init; }

    /// <summary>The result of a <see cref="TaskRunEnd.Done"/> run.</summary>
    public TaskResult? Result { get; init; }

    /// <summary>For <see cref="TaskRunEnd.Waiting"/>: when to wake up; null waits for an external resume (approval decision, follow-up reply).</summary>
    public DateTimeOffset? WakeAt { get; init; }

    /// <summary>The task's notes after the run; null keeps them.</summary>
    public string? Notes { get; init; }

    /// <summary>Why a run failed (e.g. <c>budget_exceeded</c>).</summary>
    public string? Reason { get; init; }

    /// <summary>What the run used.</summary>
    public TaskSpend Spend { get; init; } = TaskSpend.Zero;

    public static TaskRunOutcome Done(TaskResult result, string? notes = null) => new() { End = TaskRunEnd.Done, Result = result, Notes = notes };

    public static TaskRunOutcome Wait(DateTimeOffset? wakeAt, string? notes = null) => new() { End = TaskRunEnd.Waiting, WakeAt = wakeAt, Notes = notes };

    public static TaskRunOutcome Failed(string reason) => new() { End = TaskRunEnd.Failed, Reason = reason };
}

/// <summary>
/// End state of a task run.
/// </summary>
public enum TaskRunEnd
{
    /// <summary>Finished with a result.</summary>
    Done,

    /// <summary>Suspended until a wakeup time or an external resume.</summary>
    Waiting,

    /// <summary>Stopped by an error or a limit.</summary>
    Failed,
}
