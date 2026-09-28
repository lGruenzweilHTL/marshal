using System.Text.Json;
using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Tools;

/// <summary>
/// One executable, model-facing tool.
/// </summary>
public interface ITool
{
    /// <summary>Id, schema, default tier and flags.</summary>
    ToolDescriptor Descriptor { get; }

    /// <summary>
    /// Tool-specific visibility condition (e.g. the advisor is hidden in voice turns). Checked before every
    /// model call and again by the guard on every call.
    /// </summary>
    ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(true);

    /// <summary>Short, human-readable summary of a call, used in confirmation prompts.</summary>
    string Summarize(JsonElement arguments, string language) =>
        $"{Descriptor.Id} {arguments.GetRawText()}";

    /// <summary>Executes the call. Expected failures are returned as a failed result.</summary>
    Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken);
}
