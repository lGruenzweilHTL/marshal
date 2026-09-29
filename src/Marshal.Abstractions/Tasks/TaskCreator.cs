namespace Marshal.Abstractions.Tasks;

/// <summary>
/// Who created a task.
/// </summary>
public enum TaskCreator
{
    /// <summary>The user, directly or through the agent in a turn.</summary>
    User,

    /// <summary>Another task's run (a child task).</summary>
    Task,

    /// <summary>The system (seeded system tasks).</summary>
    System,
}
