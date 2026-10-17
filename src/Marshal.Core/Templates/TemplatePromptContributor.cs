using Marshal.Abstractions.Context;
using Marshal.Abstractions.Templates;

namespace Marshal.Core.Templates;

/// <summary>
/// The prompt body of the run's template (none for the built-in default).
/// </summary>
public sealed class TemplatePromptContributor(ITemplateCatalog templates) : IContextContributor
{
    public ContextStability Stability => ContextStability.Static;

    public int Order => 50;

    public Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        string prompt = templates.For(request.Context).Prompt;
        return Task.FromResult<string?>(string.IsNullOrWhiteSpace(prompt) ? null : prompt);
    }
}
