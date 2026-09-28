using System.Text.Json;
using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Tools;

/// <summary>
/// One call of a tool.
/// </summary>
/// <param name="Arguments">The arguments object (always a JSON object).</param>
/// <param name="Context">The execution context of the run.</param>
public sealed record ToolInvocation(JsonElement Arguments, MarshalExecutionContext Context);
