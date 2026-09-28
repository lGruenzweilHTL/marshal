using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Context;

/// <summary>
/// Input for building the model context of a run.
/// </summary>
/// <param name="Context">Execution context (owner, input mode, language, satellite, area).</param>
/// <param name="UserText">Text of the current user turn, if any (used by retrieval contributors).</param>
public sealed record ContextRequest(MarshalExecutionContext Context, string? UserText);
