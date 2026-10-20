using Marshal.Abstractions.Context;

namespace Marshal.Core.Context;

/// <summary>
/// Joins the sections of all contributors ordered by <see cref="IContextContributor.Stability"/>, then
/// <see cref="IContextContributor.Order"/>, and splits the prompt before the first turn section. A replay uses its
/// recorded turn context instead of the turn contributors.
/// </summary>
public sealed class ContextBuilder(IEnumerable<IContextContributor> contributors) : IContextBuilder
{
    private const string Separator = "\n\n";

    private readonly IContextContributor[] _contributors = [.. contributors.OrderBy(contributor => contributor.Stability).ThenBy(contributor => contributor.Order)];

    public async Task<SystemPrompt> BuildAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        var stable = new List<string>(_contributors.Length);
        var turn = new List<string>();
        // A replay takes the turn sections from its eval case, so memory changed since the run does not change it.
        if (request.Context.Replay is { } replay && !string.IsNullOrWhiteSpace(replay.TurnContext))
            turn.Add(replay.TurnContext.Trim());

        foreach (IContextContributor contributor in _contributors)
        {
            if (request.Context.IsReplay && contributor.Stability == ContextStability.Turn)
                continue;

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
