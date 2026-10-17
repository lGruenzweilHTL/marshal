using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tools;

/// <summary>
/// <c>agent.find_tools</c>: loads tool namespaces the turn's retrieval missed. It ranks only namespaces with tools
/// that are visible in this run, so it never reveals what the visibility filter hides. The selector offers it only
/// while retrieval narrows the run and some visible namespace is not loaded.
/// </summary>
public sealed class FindToolsTool(Func<IToolVisibilityFilter> visibility, IToolSelector selector, IOptions<ToolsOptions> options) : ITool
{
    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = NamespaceToolSelector.FindToolsId,
        Description = "Finds and loads more tools when none of your current tools fits the request. Describe what you need in a few words (e.g. \"calendar\", \"printer camera\", \"news\"). The tools it finds are available from your next step.",
        ParametersSchema = JsonDocument.Parse("""
            {"type":"object","properties":{"query":{"type":"string","description":"What the tools should be able to do."}},"required":["query"]}
            """).RootElement.Clone(),
    };

    public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(options.Value.Retrieval.Enabled);

    public Result Validate(JsonElement arguments) =>
        arguments.TryGetProperty("query", out JsonElement query) && query.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(query.GetString())
            ? Result.Success()
            : new Error("tool.invalid_arguments", "query is required.", ErrorType.Validation);

    public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        string query = invocation.Arguments.GetProperty("query").GetString()!;
        IReadOnlyList<RegisteredTool> visible = await visibility().GetVisibleToolsAsync(invocation.Context, cancellationToken);
        IReadOnlyList<ToolNamespace> found = await selector.FindAsync(query, visible, invocation.Context, cancellationToken);
        return found.Count == 0
            ? new ToolOutput("No further tools match. Answer with the tools you have, or tell the user what you cannot do.")
            : new ToolOutput(JsonSerializer.Serialize(new
            {
                loaded = found.Select(item => new { @namespace = item.Name, description = item.Description }),
                note = "These tools are available from your next step.",
            }));
    }
}
