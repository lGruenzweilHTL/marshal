using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Templates;

namespace Marshal.Testing;

/// <summary>
/// Template catalog over a mutable list; always contains the built-in <c>default</c> and <c>ai_task</c> unless replaced.
/// </summary>
public sealed class FakeTemplateCatalog : ITemplateCatalog
{
    /// <summary>Templates offered (besides the built-ins).</summary>
    public List<AgentTemplate> Items { get; } = [];

    /// <inheritdoc />
    public IReadOnlyList<AgentTemplate> Templates =>
        [.. new[] { AgentTemplate.BuiltInAiTask, AgentTemplate.BuiltInDefault }.Where(builtIn => Items.All(template => template.Name != builtIn.Name)), .. Items];

    /// <inheritdoc />
    public IReadOnlyList<ConfigFileError> Errors { get; set; } = [];

    /// <summary>A template.</summary>
    public static AgentTemplate Template(string name, string[]? tools = null, string[]? skills = null, ModelTier? tier = null, int? maxSteps = null, int? maxAdvisorCalls = null, string prompt = "") =>
        new(name, $"The {name} template.", tier, tools ?? ["*"], skills, maxSteps, maxAdvisorCalls, prompt);

    /// <inheritdoc />
    public AgentTemplate? Find(string name) => Templates.FirstOrDefault(template => template.Name == name);
}
