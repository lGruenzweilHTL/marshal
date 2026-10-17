namespace Marshal.Abstractions.Context;

/// <summary>
/// How often a context section changes. Ordered: the system prompt holds lower values first.
/// </summary>
public enum ContextStability
{
    /// <summary>The same for every run with the same configuration (base prompt, template, skill index).</summary>
    Static = 0,

    /// <summary>The same for every turn of a session (profile, output mode, satellite, pinned memory).</summary>
    Session = 1,

    /// <summary>May change every turn (retrieved memory, time, area states, task).</summary>
    Turn = 2,
}
