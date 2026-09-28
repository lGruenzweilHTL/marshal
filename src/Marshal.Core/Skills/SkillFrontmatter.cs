using YamlDotNet.Serialization;

namespace Marshal.Core.Skills;

/// <summary>
/// <c>SKILL.md</c> frontmatter per the Agent Skills specification (agentskills.io); unknown keys are errors.
/// </summary>
internal sealed class SkillFrontmatter
{
    [YamlMember(Alias = "name")]
    public string? Name { get; set; }

    [YamlMember(Alias = "description")]
    public string? Description { get; set; }

    [YamlMember(Alias = "license")]
    public string? License { get; set; }

    [YamlMember(Alias = "compatibility")]
    public string? Compatibility { get; set; }

    [YamlMember(Alias = "metadata")]
    public Dictionary<string, string>? Metadata { get; set; }

    [YamlMember(Alias = "allowed-tools")]
    public string? AllowedTools { get; set; }
}
