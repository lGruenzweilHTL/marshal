namespace Marshal.Abstractions.Tasks;

/// <summary>
/// Lifecycle state of a task.
/// </summary>
public enum AgentTaskStatus
{
    /// <summary>Waiting for its first trigger.</summary>
    Pending,

    /// <summary>A run is executing.</summary>
    Running,

    /// <summary>A run ended with a new trigger (wakeup, event, approval decision, follow-up reply).</summary>
    Waiting,

    /// <summary>Finished with a result.</summary>
    Done,

    /// <summary>Stopped by an error or a limit.</summary>
    Failed,

    /// <summary>Cancelled by the user or the kill switch.</summary>
    Cancelled,
}
