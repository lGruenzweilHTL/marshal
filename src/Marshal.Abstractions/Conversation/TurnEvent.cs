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
