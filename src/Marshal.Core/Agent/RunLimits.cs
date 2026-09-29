namespace Marshal.Core.Agent;

/// <summary>
/// Hard limits of one run (a task's remaining budget). Null limits are not enforced. Model usage counts from
/// <see cref="Abstractions.Execution.MarshalExecutionContext.Usage"/>, so subagent and advisor calls count too.
/// </summary>
public sealed record RunLimits(decimal? MaxCost = null, long? MaxTokens = null, int? MaxSteps = null, TimeSpan? MaxDuration = null);
