using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Integrations.HomeAssistant;

namespace Marshal.Demo.HomeAssistant;

/// <summary>
/// The Home Assistant service calls the demo simulates, mapped onto entity states. Other services return
/// <c>ha.service_not_supported</c>, which the model sees like any HA error.
/// </summary>
internal static class DemoServiceCalls
{
    private static readonly HashSet<string> _silent = ["assist_satellite.announce", "assist_satellite.start_conversation", "notify", "persistent_notification.create"];

    public static Result<IReadOnlyList<HaState>> Call(DemoHome home, string domain, string service, JsonObject data)
    {
        if (_silent.Contains($"{domain}.{service}") || domain == "notify")
            return Result.Success<IReadOnlyList<HaState>>([]);

        List<string> targets = Targets(data);
        var changed = new List<HaState>();
        foreach (string entityId in targets)
        {
            HaState? current = home.Snapshot.States.GetValueOrDefault(entityId);
            if (current is null)
                return new Error("ha.unknown_entity", $"There is no entity '{entityId}'.", ErrorType.NotFound);

            string entityDomain = entityId[..entityId.IndexOf('.', StringComparison.Ordinal)];
            Result<(string State, Dictionary<string, object?>? Attributes)> next = Next(domain == "homeassistant" ? entityDomain : domain, service, current, data);
            if (next.IsFailure)
                return next.Error;

            Result<HaState> set = home.Set(entityId, next.Value.State, next.Value.Attributes);
            if (set.IsFailure)
                return set.Error;
            changed.Add(set.Value);
        }

        return changed;
    }

    /// <summary>The service response HA's REST API gives: the changed states.</summary>
    public static JsonElement Response(IReadOnlyList<HaState> states) =>
        JsonSerializer.SerializeToElement(states.Select(state => new { entity_id = state.EntityId, state = state.State, attributes = state.Attributes }));

    private static List<string> Targets(JsonObject data)
    {
        JsonNode? target = data["entity_id"] ?? (data["target"] as JsonObject)?["entity_id"];
        return target switch
        {
            JsonValue value when value.TryGetValue(out string? one) => [one],
            JsonArray many => [.. many.Select(item => item?.GetValue<string>()).OfType<string>()],
            _ => [],
        };
    }

    private static Result<(string, Dictionary<string, object?>?)> Next(string domain, string service, HaState current, JsonObject data) => (domain, service) switch
    {
        ("light", "turn_on") => ("on", new Dictionary<string, object?> { ["brightness"] = Brightness(data) ?? Number(current, "brightness") ?? 255 }),
        ("light", "turn_off") => ("off", new Dictionary<string, object?> { ["brightness"] = null }),
        ("light", "toggle") => current.State == "on" ? ("off", new Dictionary<string, object?> { ["brightness"] = null }) : ("on", new Dictionary<string, object?> { ["brightness"] = 255 }),
        ("switch" or "fan" or "input_boolean", "turn_on") => ("on", null),
        ("switch" or "fan" or "input_boolean", "turn_off") => ("off", null),
        ("switch" or "fan" or "input_boolean", "toggle") => (current.State == "on" ? "off" : "on", null),
        ("climate", "set_temperature") when JsonNumbers.Number(data["temperature"]) is { } temperature => (current.State == "off" ? "heat" : current.State, new Dictionary<string, object?> { ["temperature"] = temperature }),
        ("climate", "set_hvac_mode") when data["hvac_mode"] is JsonValue mode => (mode.GetValue<string>(), null),
        ("climate", "turn_on") => ("heat", null),
        ("climate", "turn_off") => ("off", null),
        ("cover", "open_cover" or "turn_on") => ("open", new Dictionary<string, object?> { ["current_position"] = 100 }),
        ("cover", "close_cover" or "turn_off") => ("closed", new Dictionary<string, object?> { ["current_position"] = 0 }),
        ("lock", "lock") => ("locked", null),
        ("lock", "unlock") => ("unlocked", null),
        ("media_player", "media_play") => ("playing", null),
        ("media_player", "media_pause") => ("paused", null),
        ("media_player", "media_stop") => ("idle", null),
        ("media_player", "turn_off") => ("off", null),
        ("media_player", "turn_on") => ("idle", null),
        ("media_player", "volume_set") when JsonNumbers.Number(data["volume_level"]) is { } volume => (current.State, new Dictionary<string, object?> { ["volume_level"] = volume }),
        ("media_player", "play_media") => ("playing", new Dictionary<string, object?> { ["media_content_id"] = (string?)data["media_content_id"] }),
        _ => new Error("ha.service_not_supported", $"The demo does not simulate the service {domain}.{service}.", ErrorType.Validation),
    };

    private static int? Brightness(JsonObject data)
    {
        if (JsonNumbers.Number(data["brightness"]) is { } value)
            return (int)Math.Clamp(value, 0, 255);
        if (JsonNumbers.Number(data["brightness_pct"]) is { } pct)
            return (int)Math.Round(Math.Clamp(pct, 0, 100) * 255 / 100);
        return null;
    }

    private static int? Number(HaState state, string attribute) =>
        state.Attributes.TryGetProperty(attribute, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetInt32() : null;
}
