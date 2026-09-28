using System.Globalization;
using System.Text;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Templates;

namespace Marshal.Core.Skills;

/// <summary>
/// The skill index of the skills available to the run's template: names and descriptions only. Bodies are loaded on demand through <c>agent.load_skill</c>.
/// </summary>
public sealed class SkillIndexContributor(ISkillCatalog catalog, ITemplateCatalog templates) : IContextContributor
{
    public int Order => 250;

    public Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        AgentTemplate template = templates.For(request.Context);
        List<Skill> skills = [.. catalog.Skills.Where(skill => template.AllowsSkill(skill.Name))];
        if (skills.Count == 0)
            return Task.FromResult<string?>(null);

        var text = new StringBuilder("Skills (instructions for specific tasks). Before doing a task a skill covers, load it with the agent_load_skill tool and follow it:\n");
        foreach (Skill skill in skills)
            text.AppendLine(CultureInfo.InvariantCulture, $"- {skill.Name}: {skill.Description}");

        return Task.FromResult<string?>(text.ToString());
    }
}
