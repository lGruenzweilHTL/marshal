using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Agent;

/// <summary>
/// <c>agent.move_to_background(statusMessage)</c> (safe): ends the turn with the status message and continues the
/// run as a background task, like the latency cap. Visible only in interactive top-level turns and only when
/// background work is available.
/// </summary>
public sealed class MoveToBackgroundTool(IBackgroundHandoff? handoff = null) : ITool
{
    public const string Id = "agent.move_to_background";

    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = new ToolId("agent", "move_to_background"),
        Description = "Continue this request in the background when it will take long (several tool steps, waiting for something). "
            + "The user hears the status message now and gets the result later as a notification.",
        ParametersSchema = JsonDocument.Parse("""
            {"type":"object","properties":{"statusMessage":{"type":"string","description":"Short spoken message for the user now, e.g. \"I'll look into it and let you know.\""}},"required":["statusMessage"]}
            """).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
    };

    public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(handoff is not null && context.IsInteractive && !context.IsSubagent);

    public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!invocation.Arguments.TryGetProperty("statusMessage", out JsonElement message) || message.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(message.GetString()))
            return Task.FromResult<Result<ToolOutput>>(new Error("tool.invalid_arguments", "'statusMessage' is required.", ErrorType.Validation));

        invocation.Context.Control.RequestBackground(message.GetString()!.Trim());
        return Task.FromResult<Result<ToolOutput>>(new ToolOutput("Moved to the background. Continue working on the request; the user gets your final answer as a notification."));
    }
}
