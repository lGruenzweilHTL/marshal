namespace Marshal.Abstractions.Skills;

/// <summary>
/// A skill in Anthropic Agent Skills format (<c>/config/skills/&lt;name&gt;/SKILL.md</c>). Operator-written
/// configuration: trusted, never tainting.
/// </summary>
/// <param name="Name">Skill name (equals its directory name).</param>
/// <param name="Description">What the skill does and when to use it; shown in the skill index.</param>
/// <param name="Body">The Markdown body of <c>SKILL.md</c> (without frontmatter).</param>
/// <param name="Files">Additional text files by path relative to the skill directory (<c>/</c> separated).</param>
public sealed record Skill(string Name, string Description, string Body, IReadOnlyDictionary<string, string> Files);
