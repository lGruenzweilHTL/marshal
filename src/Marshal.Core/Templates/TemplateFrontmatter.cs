namespace Marshal.Core.Templates;

/// <summary>
/// Template frontmatter (camelCase keys; unknown keys are errors).
/// </summary>
internal sealed class TemplateFrontmatter
{
    public string? Name { get; set; }

    public string? Description { get; set; }

    public string? ModelTier { get; set; }

    public List<string>? Tools { get; set; }

    public List<string>? Skills { get; set; }

    public int? MaxSteps { get; set; }

    public int? MaxAdvisorCalls { get; set; }

    public TimeSpan? MinWakeupInterval { get; set; }
}
