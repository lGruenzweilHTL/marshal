using Marshal.Abstractions.Artifacts;

namespace Marshal.Abstractions.Conversation;

/// <summary>
/// An event streamed while a turn runs.
/// </summary>
public abstract record TurnEvent;

/// <summary>A chunk of reply text.</summary>
/// <param name="Text">The chunk.</param>
public sealed record DeltaEvent(string Text) : TurnEvent;

/// <summary>A progress message (e.g. "checking the printer").</summary>
/// <param name="Text">The message.</param>
public sealed record StatusEvent(string Text) : TurnEvent;

/// <summary>An artifact the turn shows (e.g. through <c>artifacts.show</c>).</summary>
/// <param name="Artifact">The artifact.</param>
public sealed record ArtifactEvent(ArtifactRef Artifact) : TurnEvent;

/// <summary>A chunk of the model's reasoning, before or between replies (web clients only).</summary>
/// <param name="Text">The chunk.</param>
public sealed record ReasoningEvent(string Text) : TurnEvent;

/// <summary>A tool call started or ended (web clients only).</summary>
/// <param name="CallId">The model's call id; the ending event repeats it.</param>
/// <param name="Tool">The tool id (e.g. <c>printer.status</c>), or the model's name for an unknown tool.</param>
/// <param name="Arguments">Short one-line summary of the arguments.</param>
/// <param name="State">Running, or how the call ended.</param>
/// <param name="DurationMs">How long the call took (ending events).</param>
/// <param name="Result">Short one-line summary of the result or the error code; null for sensitive tools.</param>
public sealed record ToolEvent(string CallId, string Tool, string Arguments, ToolEventState State, int? DurationMs = null, string? Result = null) : TurnEvent;

/// <summary>Where a tool call stands.</summary>
public enum ToolEventState
{
    Running,

    Ok,

    Error,

    /// <summary>The guard refused the call.</summary>
    Denied,

    /// <summary>The call waits for the user's confirmation.</summary>
    Pending,
}
