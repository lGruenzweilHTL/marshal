using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Marshal.Abstractions.Context;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Current states of the exposed entities in the turn's area and of exposed entities named in the user's text,
/// from the cache. Never a full dump: for anything else the model calls HA's <c>GetLiveContext</c> tool.
/// </summary>
public sealed class HaAreaContextContributor(IHomeAssistantCache cache) : IContextContributor
{
    private const int MaxAreaEntities = 40;
    private const int MaxMentionedEntities = 20;
    private const int MinNameLength = 3;

    public int Order => 350;

    public Task<string?> ContributeAsync(ContextRequest request, CancellationToken cancellationToken)
    {
        if (cache.Current is not { } snapshot)
            return Task.FromResult<string?>(null);

        string? areaId = request.Context.AreaId;
        List<HaEntity> inArea = areaId is null
            ? []
            : [.. snapshot.Entities.Values.Where(entity => entity.Exposed && entity.AreaId == areaId).OrderBy(entity => entity.EntityId, StringComparer.Ordinal).Take(MaxAreaEntities)];
        HashSet<string> listed = [.. inArea.Select(entity => entity.EntityId)];
        List<HaEntity> mentioned = string.IsNullOrWhiteSpace(request.UserText)
            ? []
            : [.. snapshot.Entities.Values
                .Where(entity => entity.Exposed && !listed.Contains(entity.EntityId) && IsMentioned(entity, snapshot, request.UserText))
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .Take(MaxMentionedEntities)];

        if (inArea.Count == 0 && mentioned.Count == 0)
            return Task.FromResult<string?>(null);

        var text = new StringBuilder();
        if (inArea.Count > 0)
        {
            string areaName = snapshot.Areas.GetValueOrDefault(areaId!)?.Name ?? areaId!;
            text.AppendLine(CultureInfo.InvariantCulture, $"Home Assistant devices in the {areaName} (current states):");
            foreach (HaEntity entity in inArea)
                text.AppendLine(CultureInfo.InvariantCulture, $"- {entity.Name} ({entity.EntityId}): {State(snapshot, entity)}");
        }

        if (mentioned.Count > 0)
        {
            text.AppendLine("Home Assistant devices mentioned by the user (current states):");
            foreach (HaEntity entity in mentioned)
            {
                string where = entity.AreaId is { } id ? $", {snapshot.Areas.GetValueOrDefault(id)?.Name ?? id}" : "";
                text.AppendLine(CultureInfo.InvariantCulture, $"- {entity.Name} ({entity.EntityId}{where}): {State(snapshot, entity)}");
            }
        }

        text.AppendLine("For other devices and areas, call the Home Assistant GetLiveContext tool instead of guessing.");
        return Task.FromResult<string?>(text.ToString());
    }

    private static bool IsMentioned(HaEntity entity, HaSnapshot snapshot, string userText)
    {
        IEnumerable<string> names = entity.Aliases.Prepend(entity.Name);
        if (snapshot.States.GetValueOrDefault(entity.EntityId)?.FriendlyName is { } friendly)
            names = names.Append(friendly);

        return names.Any(name => name.Length >= MinNameLength
            && Regex.IsMatch(userText, $@"(?<!\w){Regex.Escape(name)}(?!\w)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)));
    }

    private static string State(HaSnapshot snapshot, HaEntity entity)
    {
        if (snapshot.States.GetValueOrDefault(entity.EntityId) is not { } state)
            return "unknown";

        return state.Attributes.ValueKind == JsonValueKind.Object && state.Attributes.TryGetProperty("unit_of_measurement", out JsonElement unit) && unit.ValueKind == JsonValueKind.String
            ? $"{state.State} {unit.GetString()}"
            : state.State;
    }
}
