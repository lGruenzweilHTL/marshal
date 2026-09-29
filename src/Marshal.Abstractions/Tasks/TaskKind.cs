namespace Marshal.Abstractions.Tasks;

/// <summary>
/// How a task runs.
/// </summary>
public enum TaskKind
{
    /// <summary>Runs the agent loop headless.</summary>
    Agent,

    /// <summary>Runs a registered C# job without a model.</summary>
    System,
}
