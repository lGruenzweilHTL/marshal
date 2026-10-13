using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Models;

/// <summary>
/// Decides whether a model call requests reasoning. Voice turns never do (latency cap); text turns and background runs
/// use the model's effort. "No reasoning" leaves <see cref="ChatOptions.Reasoning"/> unset instead of sending
/// <see cref="ReasoningEffort.None"/>, so models that reject a thinking flag never see one.
/// </summary>
public static class ReasoningRequest
{
    public static ReasoningOptions? For(RoutedModel model, MarshalExecutionContext context)
    {
        if (context.InputMode == InputMode.Voice)
            return null;

        ReasoningEffort? effort = model.Reasoning switch
        {
            ModelReasoning.Low => ReasoningEffort.Low,
            ModelReasoning.Medium => ReasoningEffort.Medium,
            ModelReasoning.High => ReasoningEffort.High,
            _ => null,
        };
        return effort is null ? null : new ReasoningOptions { Effort = effort, Output = ReasoningOutput.Full };
    }
}
