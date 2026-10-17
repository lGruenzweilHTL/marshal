namespace Marshal.Abstractions.Execution;

/// <summary>
/// Kind of client that created a session or run.
/// </summary>
public enum SessionSource
{
    /// <summary>The Home Assistant Assist integration (also for typed Assist turns).</summary>
    Voice,

    /// <summary>The web chat (cookie principal).</summary>
    Web,

    /// <summary>A background task run.</summary>
    Task,

    /// <summary>Another API client with a machine token.</summary>
    Api,

    /// <summary>An AI task from a Home Assistant automation: one session per task, hidden from the chat history.</summary>
    Automation,
}
