using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Builds <see cref="HaSnapshot"/> from websocket results and applies <c>state_changed</c> events.
/// </summary>
internal static class HaSnapshotBuilder
{
    /// <summary>The raw registry results of one load.</summary>
    public sealed record Registries(JsonElement Areas, JsonElement Floors, JsonElement Devices, JsonElement Entities, JsonElement Exposed, JsonElement Entries, string? TimeZone, string? Currency = null);

    public static ImmutableDictionary<string, HaState> States(JsonElement states) =>
        states.EnumerateArray().Select(State).OfType<HaState>().ToImmutableDictionary(state => state.EntityId, StringComparer.Ordinal);

    /// <summary>Entity ids exposed to Assist.</summary>
    public static HashSet<string> ExposedIds(JsonElement exposed) =>
        exposed.TryGetProperty("exposed_entities", out JsonElement entities)
            ? [.. entities.EnumerateObject().Where(entity => entity.Value.TryGetProperty("conversation", out JsonElement flag) && flag.ValueKind == JsonValueKind.True).Select(entity => entity.Name)]
            : [];

    public static HaSnapshot Build(Registries registries, ImmutableDictionary<string, HaState> states)
    {
        var areas = registries.Areas.EnumerateArray()
            .Select(area => new HaArea(Text(area, "area_id")!, Text(area, "name") ?? "", Aliases(area), Text(area, "floor_id")))
            .ToImmutableDictionary(area => area.Id, StringComparer.Ordinal);
        var floors = registries.Floors.EnumerateArray()
            .Select(floor => new HaFloor(Text(floor, "floor_id")!, Text(floor, "name") ?? "", Aliases(floor)))
            .ToImmutableDictionary(floor => floor.Id, StringComparer.Ordinal);
        var devices = registries.Devices.EnumerateArray()
            .Select(device => new HaDevice(Text(device, "id")!, Text(device, "name_by_user") ?? Text(device, "name"), Text(device, "area_id")))
            .ToImmutableDictionary(device => device.Id, StringComparer.Ordinal);

        var registered = registries.Entities.EnumerateArray().ToDictionary(entry => Text(entry, "entity_id")!, StringComparer.Ordinal);
        HashSet<string> exposed = ExposedIds(registries.Exposed);
        ImmutableDictionary<string, HaEntity>.Builder entities = ImmutableDictionary.CreateBuilder<string, HaEntity>(StringComparer.Ordinal);
        foreach (string id in registered.Keys.Union(states.Keys, StringComparer.Ordinal))
        {
            JsonElement? entry = registered.TryGetValue(id, out JsonElement found) ? found : null;
            JsonElement? extended = registries.Entries.ValueKind == JsonValueKind.Object && registries.Entries.TryGetProperty(id, out JsonElement more) && more.ValueKind == JsonValueKind.Object ? more : null;
            HaState? state = states.GetValueOrDefault(id);
            string? deviceId = entry is { } e ? Text(e, "device_id") : null;
            string? area = (entry is { } withArea ? Text(withArea, "area_id") : null) ?? (deviceId is not null ? devices.GetValueOrDefault(deviceId)?.AreaId : null);
            string name = (entry is { } named ? Text(named, "name") : null)
                ?? state?.FriendlyName
                ?? (entry is { } original ? Text(original, "original_name") : null)
                ?? id[(id.IndexOf('.', StringComparison.Ordinal) + 1)..];
            string? deviceClass = (extended is { } x ? Text(x, "device_class") ?? Text(x, "original_device_class") : null)
                ?? (state is not null ? Text(state.Attributes, "device_class") : null);
            entities[id] = new HaEntity(id, name, extended is { } withAliases ? Aliases(withAliases) : [], area, deviceId, deviceClass, exposed.Contains(id));
        }

        return new HaSnapshot
        {
            Areas = areas,
            Floors = floors,
            Devices = devices,
            Entities = entities.ToImmutable(),
            States = states,
            TimeZone = registries.TimeZone,
            Currency = registries.Currency,
        };
    }

    /// <summary>Applies a <c>state_changed</c> event; an older state than the cached one is ignored.</summary>
    public static HaSnapshot ApplyStateChanged(HaSnapshot snapshot, JsonElement data)
    {
        if (Text(data, "entity_id") is not { } id)
            return snapshot;

        if (!data.TryGetProperty("new_state", out JsonElement newState) || newState.ValueKind != JsonValueKind.Object)
            return snapshot with { States = snapshot.States.Remove(id) };

        if (State(newState) is not { } state)
            return snapshot;

        if (snapshot.States.TryGetValue(id, out HaState? existing) && existing.LastUpdated is { } before && state.LastUpdated is { } after && after < before)
            return snapshot;

        return snapshot with { States = snapshot.States.SetItem(id, state) };
    }

    private static HaState? State(JsonElement state)
    {
        if (Text(state, "entity_id") is not { } id || Text(state, "state") is not { } value)
            return null;

        DateTimeOffset? updated = Text(state, "last_updated") is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset parsed) ? parsed : null;
        JsonElement attributes = state.TryGetProperty("attributes", out JsonElement found) ? found.Clone() : default;
        return new HaState(id, value, attributes, updated);
    }

    private static List<string> Aliases(JsonElement entry) =>
        entry.TryGetProperty("aliases", out JsonElement aliases) && aliases.ValueKind == JsonValueKind.Array
            ? [.. aliases.EnumerateArray().Where(alias => alias.ValueKind == JsonValueKind.String).Select(alias => alias.GetString()!)]
            : [];

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
