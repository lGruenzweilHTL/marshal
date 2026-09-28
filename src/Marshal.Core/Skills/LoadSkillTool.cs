using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Skills;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Skills;

/// <summary>
/// <c>agent.load_skill(name, file?)</c>: a skill's body (with the list of its files) or one of its files. Skills are
/// trusted configuration, so the output never taints the run. Only skills of the run's template; hidden when there are none.
/// </summary>
public sealed class LoadSkillTool(ISkillCatalog catalog, ITemplateCatalog templates) : ITool
{
    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = new ToolId("agent", "load_skill"),
        Description = "Loads the instructions of a skill from the skill index, or one of its files.",
        ParametersSchema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "name":{"type":"string","description":"Skill name from the skill index."},
              "file":{"type":"string","description":"Optional file of the skill, relative to it (e.g. references/format.md)."}},
             "required":["name"]}
            """).RootElement.Clone(),
        DefaultTier = RiskTier.Safe,
    };

    public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Available(context).Any());

    public string Summarize(JsonElement arguments, string language) =>
        $"load skill {Text(arguments, "name")}";

    public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        string? name = Text(invocation.Arguments, "name");
        List<Skill> available = [.. Available(invocation.Context)];
        if (available.Find(candidate => candidate.Name == name) is not { } skill)
            return Task.FromResult<Result<ToolOutput>>(new Error("skill.not_found", $"There is no skill '{name}'. Available: {string.Join(", ", available.Select(candidate => candidate.Name))}.", ErrorType.NotFound));

        if (Text(invocation.Arguments, "file") is { } file)
        {
            return Task.FromResult<Result<ToolOutput>>(skill.Files.TryGetValue(file, out string? content)
                ? new ToolOutput(content, $"skill:{skill.Name}/{file}")
                : new Error("skill.file_not_found", $"Skill '{skill.Name}' has no file '{file}'. Files: {string.Join(", ", skill.Files.Keys)}.", ErrorType.NotFound));
        }

        var text = new StringBuilder(skill.Body);
        if (skill.Files.Count > 0)
            text.Append("\n\nFiles of this skill (load with agent_load_skill and file): ").Append(string.Join(", ", skill.Files.Keys));

        return Task.FromResult<Result<ToolOutput>>(new ToolOutput(text.ToString(), $"skill:{skill.Name}"));
    }

    private IEnumerable<Skill> Available(MarshalExecutionContext context)
    {
        AgentTemplate template = templates.For(context);
        return catalog.Skills.Where(skill => template.AllowsSkill(skill.Name));
    }

    private static string? Text(JsonElement arguments, string property) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
