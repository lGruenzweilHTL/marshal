using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Music;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.HomeAssistant.Music;

/// <summary>
/// Loads the music namespace when the turn names a configured speaker: its configured name (underscores read as
/// spaces) or the friendly name of its media player in Home Assistant.
/// </summary>
public sealed class MusicNamespaceHints(ISpeakerDirectory speakers, IHomeAssistantCache cache) : IToolNamespaceHints
{
    public Task<IReadOnlyList<string>> GetHintsAsync(string turnText, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(turnText))
            return Task.FromResult<IReadOnlyList<string>>([]);

        HaSnapshot? snapshot = cache.Current;
        bool named = speakers.Names.Any(name =>
        {
            List<string> names = [name, name.Replace('_', ' ')];
            if (speakers.ByName(name) is { } speaker && snapshot?.States.GetValueOrDefault(speaker.Entity)?.FriendlyName is { } friendly)
                names.Add(friendly);
            return HaNames.AnyMentioned(names, turnText);
        });
        return Task.FromResult<IReadOnlyList<string>>(named ? ["music"] : []);
    }
}
