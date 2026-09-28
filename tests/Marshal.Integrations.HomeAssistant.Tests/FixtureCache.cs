using System.Text.Json;

namespace Marshal.Integrations.HomeAssistant.Tests;

/// <summary>A cache holding the snapshot of the recorded Home Assistant session.</summary>
internal sealed class FixtureCache : IHomeAssistantCache
{
    private static readonly Lazy<HaSnapshot> _recorded = new(Load);

    public HaSnapshot? Current { get; set; } = _recorded.Value;

    public string? LastError { get; set; }

    public static HaSnapshot Recorded => _recorded.Value;

    private static HaSnapshot Load()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "websocket-session.json")));
        var types = new Dictionary<int, string>();
        var results = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (JsonElement entry in document.RootElement.EnumerateArray())
        {
            JsonElement message = entry.GetProperty("message");
            if (entry.GetProperty("from").GetString() == "client" && message.TryGetProperty("id", out JsonElement id))
                types[id.GetInt32()] = message.GetProperty("type").GetString()!;
            else if (message.GetProperty("type").GetString() == "result" && types.TryGetValue(message.GetProperty("id").GetInt32(), out string? type))
                results.TryAdd(type, message.GetProperty("result").Clone());
        }

        var registries = new HaSnapshotBuilder.Registries(
            results["config/area_registry/list"],
            results["config/floor_registry/list"],
            results["config/device_registry/list"],
            results["config/entity_registry/list"],
            results["homeassistant/expose_entity/list"],
            results["config/entity_registry/get_entries"],
            results["get_config"].GetProperty("time_zone").GetString());
        return HaSnapshotBuilder.Build(registries, HaSnapshotBuilder.States(results["get_states"]));
    }
}
