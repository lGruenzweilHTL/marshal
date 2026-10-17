using Marshal.Abstractions.Context;

namespace Marshal.Core.Context;

/// <summary>
/// Joins the sections of all contributors ordered by <see cref="IContextContributor.Stability"/>, then
/// <see cref="IContextContributor.Order"/>, and splits the prompt before the first turn section.
/// </summary>
public sealed class ContextBuilder(IEnumerable<IContextContributor> contributors) : IContextBuilder
{
    private const string Separator = "\n\n";

    private readonly IContextContributor[] _contributors = [.. contributors.OrderBy(contributor => contributor.Stability).ThenBy(contributor => contributor.Order)];

    public async Task<SystemPrompt> BuildAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        var stable = new List<string>(_contributors.Length);
        var turn = new List<string>();
        foreach (IContextContributor contributor in _contributors)
        {
            string? section = await contributor.ContributeAsync(request, cancellationToken);
            if (!string.IsNullOrWhiteSpace(section))
                (contributor.Stability == ContextStability.Turn ? turn : stable).Add(section.Trim());
        }

        string stableText = string.Join(Separator, stable);
        string turnText = string.Join(Separator, turn);
        return stableText.Length > 0 && turnText.Length > 0
            ? new SystemPrompt(stableText + Separator, turnText)
            : new SystemPrompt(stableText, turnText);
    }

    public async Task<string> BuildSystemPromptAsync(ContextRequest request, CancellationToken cancellationToken) =>
        (await BuildAsync(request, cancellationToken)).Text;
}
