using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Templates;

/// <summary>
/// Agent templates from <c>/config/templates/</c> plus the built-in <c>default</c>, hot-reloaded.
/// </summary>
public interface ITemplateCatalog
{
    /// <summary>All valid templates including <c>default</c>, ordered by name.</summary>
    IReadOnlyList<AgentTemplate> Templates { get; }

    /// <summary>Template files rejected by the last load.</summary>
    IReadOnlyList<ConfigFileError> Errors { get; }

    /// <summary>A template by name; null when unknown.</summary>
    AgentTemplate? Find(string name);

    /// <summary>The template of a run (its <see cref="MarshalExecutionContext.Template"/>, else <c>default</c>).</summary>
    AgentTemplate For(MarshalExecutionContext context) => Find(context.Template) ?? Find(AgentTemplate.DefaultName) ?? AgentTemplate.BuiltInDefault;
}
