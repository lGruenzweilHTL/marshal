namespace Marshal.Abstractions.Execution;

/// <summary>
/// Where untrusted content entered a run.
/// </summary>
/// <param name="Kind">Source kind: <c>mail</c>, <c>rss</c>, <c>notes</c>, <c>calendar</c>, <c>web</c> or <c>mcp:&lt;server&gt;</c>.</param>
/// <param name="FirstStep">Sequence number of the first run step that brought in content of this kind.</param>
public sealed record TaintSource(string Kind, int FirstStep);
