namespace Marshal.Abstractions.Executors;

/// <summary>
/// Lifecycle state of an executor session.
/// </summary>
public enum ExecutorSessionStatus
{
    /// <summary>Requested from the executor.</summary>
    Starting,

    /// <summary>Working.</summary>
    Running,

    /// <summary>Finished successfully.</summary>
    Done,

    /// <summary>Finished with an error.</summary>
    Failed,

    /// <summary>Stopped before it finished.</summary>
    Cancelled,
}
