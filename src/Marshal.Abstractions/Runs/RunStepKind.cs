namespace Marshal.Abstractions.Runs;

/// <summary>
/// Kind of a recorded run step.
/// </summary>
public enum RunStepKind
{
    /// <summary>Context build (system prompt, inputs).</summary>
    Context,

    /// <summary>One model call.</summary>
    ModelCall,

    /// <summary>One tool call, including its guard decision.</summary>
    ToolCall,

    /// <summary>The final reply.</summary>
    Reply,
}
