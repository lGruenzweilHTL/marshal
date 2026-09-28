using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Marshal.Core.Context;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Templates;

/// <summary>
/// <c>agent.delegate(template, task)</c>: runs a nested agent loop with the template and returns its final text.
/// The subagent shares the caller's owner, satellite, tools and taint (so taint flows back), is recorded in the
/// caller's run and never runs confirm-tier actions. Hidden at the depth limit and when no template is delegable.
/// </summary>
/// <param name="runner">Resolved per call; the runner itself depends on the tool registry that offers this tool.</param>
/// <param name="templates">Templates to delegate to.</param>
/// <param name="options">Depth limit.</param>
public sealed class DelegateTool(Func<IAgentRunner> runner, ITemplateCatalog templates, IOptions<AgentOptions> options) : ITool
{
    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = new ToolId("agent", "delegate"),
        Description = "Hands a self-contained task to a subagent using an agent template from the template list and returns its answer. The subagent cannot ask the user anything or run actions that need confirmation; it reports them instead.",
        ParametersSchema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "template":{"type":"string","description":"Template name from the template list."},
              "task":{"type":"string","description":"Complete task description; the subagent sees nothing else of this conversation."}},
             "required":["template","task"]}
            """).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
    };

    /// <summary>Templates a run at <paramref name="context"/>'s depth may delegate to; empty at the depth limit.</summary>
    public static IReadOnlyList<AgentTemplate> Delegable(ITemplateCatalog templates, AgentOptions options, MarshalExecutionContext context) =>
        context.Depth >= options.MaxDelegationDepth
            ? []
            : [.. templates.Templates.Where(template => template.Name != AgentTemplate.DefaultName)];

    public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Delegable(templates, options.Value, context).Count > 0);

    public string Summarize(JsonElement arguments, string language) =>
        $"delegate to {Text(arguments, "template")}";

    public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = invocation.Context;
        IReadOnlyList<AgentTemplate> delegable = Delegable(templates, options.Value, context);
        string? name = Text(invocation.Arguments, "template");
        if (delegable.FirstOrDefault(template => template.Name == name) is not { } target)
            return new Error("agent.template_not_found", $"There is no template '{name}'. Available: {string.Join(", ", delegable.Select(template => template.Name))}.", ErrorType.NotFound);

        string? task = Text(invocation.Arguments, "task");
        if (string.IsNullOrWhiteSpace(task))
            return new Error("agent.task_missing", "Describe the task for the subagent.", ErrorType.Validation);

        // Same owner, satellite, run, tools and taint object; own template, depth and per-run counters.
        MarshalExecutionContext child = context with { Template = target.Name, Depth = context.Depth + 1, Counters = new RunCounters() };
        AgentRunResult result = await runner().RunAsync(new AgentRunRequest { Context = child, Trigger = "delegate", UserText = task }, cancellationToken);
        if (result.Outcome != RunOutcome.Ok)
            return new Error("agent.delegate_failed", $"The subagent stopped ({result.OutcomeReason}): {result.Reply}", ErrorType.Failure);

        // A tainted subagent (tainted itself or by its caller) may have been steered by untrusted data.
        return context.Taint.IsTainted
            ? new ToolOutput(UntrustedContent.Wrap("subagent", result.Reply))
            : new ToolOutput(result.Reply);
    }

    private static string? Text(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
