using System.Text.Json;
using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Guard;

/// <summary>
/// A tool call requested by the model.
/// </summary>
/// <param name="ModelName">Model-facing tool name.</param>
/// <param name="Arguments">Arguments object.</param>
/// <param name="Context">Execution context of the run.</param>
/// <param name="OfferedModelNames">Names in the tool list sent with the model call that produced this call.</param>
public sealed record GuardRequest(string ModelName, JsonElement Arguments, MarshalExecutionContext Context, IReadOnlySet<string> OfferedModelNames);
