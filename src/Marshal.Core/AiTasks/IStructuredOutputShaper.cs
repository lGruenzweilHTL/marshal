using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;

namespace Marshal.Core.AiTasks;

/// <summary>
/// Turns a run's final reply into JSON that matches a JSON Schema.
/// </summary>
public interface IStructuredOutputShaper
{
    /// <summary>
    /// Shapes <see cref="StructuredOutputRequest.Reply"/> into an instance of the schema. Model calls are recorded as
    /// steps of the run in <paramref name="context"/>. Fails with <see cref="StructuredOutput.InvalidOutputCode"/> when
    /// the output still does not validate after one repair.
    /// </summary>
    Task<Result<JsonElement>> ShapeAsync(StructuredOutputRequest request, MarshalExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>What to shape.</summary>
/// <param name="Model">The run's model.</param>
/// <param name="Instructions">The task's instructions.</param>
/// <param name="Reply">The run's final reply.</param>
/// <param name="Schema">JSON Schema the result must match.</param>
public sealed record StructuredOutputRequest(RoutedModel Model, string Instructions, string Reply, JsonElement Schema);

/// <summary>Error codes of structured output.</summary>
public static class StructuredOutput
{
    public const string InvalidOutputCode = "ai_task.invalid_output";
}
