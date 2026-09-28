using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Runs;

/// <summary>
/// How a run ended.
/// </summary>
/// <param name="Outcome">Outcome.</param>
/// <param name="Reason">Why (e.g. <c>latency_cap</c>, <c>step_limit</c>), if not a normal end.</param>
/// <param name="TaintSources">Taint sources of the run (empty when untainted).</param>
public sealed record RunCompletion(RunOutcome Outcome, string? Reason, IReadOnlyList<TaintSource> TaintSources);
