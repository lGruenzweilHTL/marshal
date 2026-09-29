using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Guard;

/// <summary>
/// Executes a confirmed pending action: exactly the frozen tool and arguments. A tool that is gone or now forbidden
/// is not executed.
/// </summary>
public static class FrozenActions
{
    public static async Task<(RegisteredTool? Tool, Result<ToolOutput> Result)> ExecuteAsync(
        IToolRegistry registry, IRiskEngine riskEngine, PendingActionRecord action, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        RegisteredTool? tool = registry.Current.FindById(action.Tool);
        if (tool is null)
            return (null, new Error("tool.unknown", $"The tool '{action.Tool}' is no longer available.", ErrorType.NotFound));

        using var document = JsonDocument.Parse(action.ArgumentsJson);
        JsonElement arguments = document.RootElement.Clone();
        RiskAssessment assessment = await riskEngine.AssessAsync(tool, arguments, cancellationToken);
        if (assessment.Tier == RiskTier.Forbidden)
            return (tool, new Error("tool.forbidden", "This action is no longer allowed.", ErrorType.Forbidden));

        Result<ToolOutput> result = await tool.Tool.InvokeAsync(new ToolInvocation(arguments, context), cancellationToken);
        return (tool, result);
    }
}
