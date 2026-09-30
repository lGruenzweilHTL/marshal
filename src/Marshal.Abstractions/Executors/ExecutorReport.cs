namespace Marshal.Abstractions.Executors;

/// <summary>
/// A callback from an executor. Everything in it is untrusted: it comes from a model working on external content.
/// </summary>
/// <param name="Type"><c>progress</c>, <c>completed</c>, <c>failed</c> or <c>cancelled</c>.</param>
/// <param name="Message">Progress text or the failure.</param>
/// <param name="Branch">Branch holding the change (completed coding sessions).</param>
/// <param name="Diff">Unified diff of the change.</param>
/// <param name="Summary">The executor's final answer.</param>
/// <param name="CostUsd">Model cost the executor reported.</param>
public sealed record ExecutorReport(string Type, string? Message = null, string? Branch = null, string? Diff = null, string? Summary = null, decimal? CostUsd = null);
