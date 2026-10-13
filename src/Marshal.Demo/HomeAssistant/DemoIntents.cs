using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Integrations.HomeAssistant;

namespace Marshal.Demo.HomeAssistant;

/// <summary>
/// The Assist intents the demo handles, answering in HA's intent response shape (<c>response_type</c>, plain
/// speech, matched entities). Targets are matched like HA's intent matching: every given slot (<c>name</c>,
/// <c>area</c>, <c>floor</c>, <c>domain</c>, <c>device_class</c>) narrows the exposed entities.
/// </summary>
internal static class DemoIntents
{
    private static readonly string[] _switchable = ["light", "switch", "fan", "input_boolean"];

    public static JsonElement Handle(DemoHome home, DateTimeOffset now, string intent, JsonObject data, string? deviceId) => intent switch
    {
        "HassTurnOn" => Switch(home, data, "turn_on", "Turned on"),
        "HassTurnOff" => Switch(home, data, "turn_off", "Turned off"),
        "HassLightSet" => LightSet(home, data),
        "HassClimateSetTemperature" => ClimateSet(home, data),
        "HassStartTimer" => StartTimer(home, now, data, deviceId),
        "HassCancelTimer" => CancelTimer(home, now, data, deviceId),
        "HassTimerStatus" => TimerStatus(home, now, deviceId),
        _ => Error("no_intent_match", "Sorry, the demo cannot do that."),
    };

    /// <summary>The exposed entities the slots address, or an empty list when a slot matches nothing.</summary>
    public static List<HaEntity> Match(HaSnapshot snapshot, JsonObject data, IReadOnlyCollection<string>? defaultDomains)
    {
        IEnumerable<HaEntity> entities = snapshot.Entities.Values.Where(entity => entity.Exposed);
        if (Text(data, "name") is { } name)
            entities = entities.Where(entity => Same(name, entity.Name, entity.EntityId) || snapshot.States.GetValueOrDefault(entity.EntityId)?.FriendlyName is { } friendly && Same(name, friendly));
        if (Text(data, "area") is { } areaName)
        {
            HaArea? area = snapshot.Areas.Values.FirstOrDefault(candidate => Same(areaName, [candidate.Id, candidate.Name, .. candidate.Aliases]));
            if (area is null)
                return [];
            entities = entities.Where(entity => entity.AreaId == area.Id);
        }

        if (Text(data, "floor") is { } floorName)
        {
            HaFloor? floor = snapshot.Floors.Values.FirstOrDefault(candidate => Same(floorName, [candidate.Id, candidate.Name, .. candidate.Aliases]));
            if (floor is null)
                return [];
            entities = entities.Where(entity => entity.AreaId is { } id && snapshot.Areas.GetValueOrDefault(id)?.FloorId == floor.Id);
        }

        List<string> domains = Texts(data, "domain");
        if (domains.Count > 0)
            entities = entities.Where(entity => domains.Contains(entity.Domain, StringComparer.OrdinalIgnoreCase));
        else if (Text(data, "name") is null && defaultDomains is not null)
            entities = entities.Where(entity => defaultDomains.Contains(entity.Domain));

        List<string> classes = Texts(data, "device_class");
        if (classes.Count > 0)
            entities = entities.Where(entity => entity.DeviceClass is { } deviceClass && classes.Contains(deviceClass, StringComparer.OrdinalIgnoreCase));

        return [.. entities.OrderBy(entity => entity.EntityId, StringComparer.Ordinal)];
    }

    private static JsonElement Switch(DemoHome home, JsonObject data, string service, string verb)
    {
        List<HaEntity> targets = Match(home.Snapshot, data, _switchable);
        if (targets.Count == 0)
            return Error("no_valid_targets", "No device matched that name or area.");

        var done = new List<HaEntity>();
        foreach (HaEntity entity in targets)
        {
            string domain = entity.Domain;
            string mapped = (domain, service) switch
            {
                ("cover", "turn_on") => "open_cover",
                ("cover", "turn_off") => "close_cover",
                ("lock", "turn_on") => "lock",
                ("lock", "turn_off") => "unlock",
                _ => service,
            };
            if (DemoServiceCalls.Call(home, domain, mapped, new JsonObject { ["entity_id"] = entity.EntityId }).IsSuccess)
                done.Add(entity);
        }

        return done.Count == 0
            ? Error("failed_to_handle", "The device did not respond.")
            : Done($"{verb} {Describe(done)}.", done);
    }

    private static JsonElement LightSet(DemoHome home, JsonObject data)
    {
        List<HaEntity> targets = [.. Match(home.Snapshot, data, ["light"]).Where(entity => entity.Domain == "light")];
        if (targets.Count == 0)
            return Error("no_valid_targets", "No light matched that name or area.");
        if (JsonNumbers.Number(data["brightness"]) is not { } percent)
            return Error("invalid_slot", "Brightness is missing.");

        foreach (HaEntity entity in targets)
            DemoServiceCalls.Call(home, "light", percent <= 0 ? "turn_off" : "turn_on", new JsonObject { ["entity_id"] = entity.EntityId, ["brightness_pct"] = percent });
        return Done(string.Create(CultureInfo.InvariantCulture, $"Brightness set to {percent:0} %."), targets);
    }

    private static JsonElement ClimateSet(DemoHome home, JsonObject data)
    {
        List<HaEntity> targets = [.. Match(home.Snapshot, data, ["climate"]).Where(entity => entity.Domain == "climate")];
        if (targets.Count == 0)
            return Error("no_valid_targets", "No thermostat matched that name or area.");
        if (JsonNumbers.Number(data["temperature"]) is not { } temperature)
            return Error("invalid_slot", "Temperature is missing.");

        foreach (HaEntity entity in targets)
            DemoServiceCalls.Call(home, "climate", "set_temperature", new JsonObject { ["entity_id"] = entity.EntityId, ["temperature"] = temperature });
        return Done(string.Create(CultureInfo.InvariantCulture, $"Temperature set to {temperature:0.#} °C."), targets);
    }

    private static JsonElement StartTimer(DemoHome home, DateTimeOffset now, JsonObject data, string? deviceId)
    {
        TimeSpan duration = TimeSpan.FromHours(Number(data, "hours")) + TimeSpan.FromMinutes(Number(data, "minutes")) + TimeSpan.FromSeconds(Number(data, "seconds"));
        if (duration <= TimeSpan.Zero)
            return Error("invalid_slot", "The timer needs a duration.");

        string? name = Text(data, "name");
        home.Timers.Add(new DemoTimer(name, deviceId, duration, now));
        return Done($"{(name is null ? "Timer" : $"{Capitalize(name)} timer")} started for {Span(duration)}.", []);
    }

    private static JsonElement CancelTimer(DemoHome home, DateTimeOffset now, JsonObject data, string? deviceId)
    {
        IReadOnlyList<DemoTimer> running = home.Timers.Running(deviceId, now);
        string? name = Text(data, "name");
        List<DemoTimer> matching = [.. running.Where(timer => name is null || string.Equals(timer.Name, name, StringComparison.OrdinalIgnoreCase))];
        if (matching.Count == 0)
            return Error("timer_not_found", name is null ? "No timer is running." : $"There is no {name} timer.");
        if (matching.Count > 1)
            return Error("multiple_timers_matched", "Several timers are running; say which one.");

        home.Timers.Remove(matching[0]);
        return Done("Timer cancelled.", []);
    }

    private static JsonElement TimerStatus(DemoHome home, DateTimeOffset now, string? deviceId)
    {
        IReadOnlyList<DemoTimer> running = home.Timers.Running(deviceId, now);
        if (running.Count == 0)
            return Done("No timers are running.", []);

        return Done(string.Join(" ", running.Select(timer => $"{(timer.Name is null ? "Timer" : Capitalize(timer.Name))}: {Span(timer.EndsAt - now)} left.")), []);
    }

    private static string Describe(List<HaEntity> entities) =>
        entities.Count == 1 ? $"the {entities[0].Name.ToLowerInvariant()}" : $"{entities.Count} devices";

    private static JsonElement Done(string speech, IEnumerable<HaEntity> entities) => JsonSerializer.SerializeToElement(new
    {
        response_type = "action_done",
        speech = new { plain = new { speech } },
        data = new { success = entities.Select(entity => new { type = "entity", name = entity.Name, id = entity.EntityId }), failed = Array.Empty<object>() },
    });

    private static JsonElement Error(string code, string speech) => JsonSerializer.SerializeToElement(new
    {
        response_type = "error",
        speech = new { plain = new { speech } },
        data = new { code },
    });

    private static string Span(TimeSpan span)
    {
        var parts = new List<string>();
        if (span.Hours > 0)
            parts.Add($"{span.Hours} hour{(span.Hours == 1 ? "" : "s")}");
        if (span.Minutes > 0)
            parts.Add($"{span.Minutes} minute{(span.Minutes == 1 ? "" : "s")}");
        if (span.Seconds > 0 && span.Hours == 0)
            parts.Add($"{span.Seconds} second{(span.Seconds == 1 ? "" : "s")}");
        return parts.Count == 0 ? "less than a second" : string.Join(" and ", parts);
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    private static bool Same(string wanted, params IEnumerable<string> candidates) =>
        candidates.Any(candidate => string.Equals(candidate.Trim(), wanted.Trim(), StringComparison.OrdinalIgnoreCase));

    private static double Number(JsonObject data, string slot) => JsonNumbers.Number(data[slot]) ?? 0;

    private static string? Text(JsonObject data, string slot) =>
        data[slot] is JsonValue value && value.TryGetValue(out string? text) && !string.IsNullOrWhiteSpace(text) ? text : null;

    private static List<string> Texts(JsonObject data, string slot) => data[slot] switch
    {
        JsonValue value when value.TryGetValue(out string? one) => [one],
        JsonArray many => [.. many.Select(item => item?.GetValue<string>()).OfType<string>()],
        _ => [],
    };
}
