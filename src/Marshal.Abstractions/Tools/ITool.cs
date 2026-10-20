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

    /// <summary>
    /// Pin approval of the tool's current definition (MCP tools). Anything but <see cref="ToolPinState.Approved"/>
    /// hides the tool and makes the guard deny it.
    /// </summary>
    ToolPinState PinState => ToolPinState.Approved;

    /// <summary>
    /// Argument checks that must hold before any tier applies (e.g. an allowlist). The guard denies a failing call
    /// without asking for confirmation.
    /// </summary>
    Result Validate(JsonElement arguments) => Result.Success();

    /// <summary>Short, human-readable summary of a call, used in confirmation prompts.</summary>
    string Summarize(JsonElement arguments, string language) =>
        $"{Descriptor.Id} {arguments.GetRawText()}";

    /// <summary>
    /// Summary of a call in <paramref name="context"/>, for tools whose confirmation text depends on the run (e.g. the
    /// tool policy a new task gets). Defaults to <see cref="Summarize(JsonElement, string)"/> in the run's language.
    /// </summary>
    string Summarize(JsonElement arguments, MarshalExecutionContext context) => Summarize(arguments, context.Language);

    /// <summary>Executes the call. Expected failures are returned as a failed result.</summary>
    Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken);
}
