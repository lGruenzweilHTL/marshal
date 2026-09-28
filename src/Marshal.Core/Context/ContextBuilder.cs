using Marshal.Abstractions.Context;

namespace Marshal.Core.Context;

/// <summary>
/// Joins the sections of all contributors in <see cref="IContextContributor.Order"/>.
/// </summary>
public sealed class ContextBuilder(IEnumerable<IContextContributor> contributors) : IContextBuilder
{
    private readonly IContextContributor[] _contributors = [.. contributors.OrderBy(contributor => contributor.Order)];

    public async Task<string> BuildSystemPromptAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        var sections = new List<string>(_contributors.Length);
        foreach (IContextContributor contributor in _contributors)
        {
            string? section = await contributor.ContributeAsync(request, cancellationToken);
            if (!string.IsNullOrWhiteSpace(section))
                sections.Add(section.Trim());
        }

        return string.Join("\n\n", sections);
    }
}
