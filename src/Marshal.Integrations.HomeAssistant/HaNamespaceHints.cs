using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Loads the Home Assistant namespace when the turn names an exposed entity (name, alias, friendly name) or an area
/// (name or alias). Short German commands ("mach das Nachtlicht aus") often miss the namespace description by
/// embedding but name the device.
/// </summary>
public sealed class HaNamespaceHints(IHomeAssistantCache cache) : IToolNamespaceHints
{
    /// <summary>Namespace of Home Assistant's MCP server tools.</summary>
    public const string Namespace = "ha";

    public Task<IReadOnlyList<string>> GetHintsAsync(string turnText, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        if (cache.Current is not { } snapshot || string.IsNullOrWhiteSpace(turnText))
            return Task.FromResult<IReadOnlyList<string>>([]);

        bool named = snapshot.Areas.Values.Any(area => HaNames.AnyMentioned(area.Aliases.Prepend(area.Name), turnText))
            || snapshot.Entities.Values.Any(entity => entity.Exposed && HaNames.AnyMentioned(HaNames.Of(entity, snapshot), turnText));
        return Task.FromResult<IReadOnlyList<string>>(named ? [Namespace] : []);
    }
}
