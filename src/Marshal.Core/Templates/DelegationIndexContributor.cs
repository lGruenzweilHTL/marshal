using System.Globalization;
using System.Text;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Core.Agent;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Templates;

/// <summary>
/// The template list for <c>agent.delegate</c>: names and descriptions of the delegable templates, when the run's
/// template allows delegating and the depth limit is not reached.
/// </summary>
public sealed class DelegationIndexContributor(ITemplateCatalog templates, IOptions<AgentOptions> options) : IContextContributor
{
    private static readonly ToolId _delegate = new("agent", "delegate");

    public int Order => 260;

    public Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        if (!templates.For(request.Context).Allows(_delegate))
            return Task.FromResult<string?>(null);

        IReadOnlyList<AgentTemplate> delegable = DelegateTool.Delegable(templates, options.Value, request.Context);
        if (delegable.Count == 0)
            return Task.FromResult<string?>(null);

        var text = new StringBuilder("Agent templates. Hand self-contained subtasks to a subagent with the agent_delegate tool:\n");
        foreach (AgentTemplate template in delegable)
            text.AppendLine(CultureInfo.InvariantCulture, $"- {template.Name}: {template.Description}");

        return Task.FromResult<string?>(text.ToString());
    }
}
