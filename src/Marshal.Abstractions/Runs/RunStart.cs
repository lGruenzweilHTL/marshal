using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Runs;

/// <summary>
/// Data recorded when a run starts. Owner, session and per-turn data come from the execution context.
/// </summary>
/// <param name="Context">Execution context of the run.</param>
/// <param name="Trigger">What started the run (<c>turn</c>, <c>confirmation</c>, later task triggers).</param>
public sealed record RunStart(MarshalExecutionContext Context, string Trigger);
