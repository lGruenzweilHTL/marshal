using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// <c>places.find</c> lists Home Assistant zones with the state value the owner's person entity takes in each, so a
/// location watch filters on the exact value. <c>places.create_here</c> creates a zone at the owner's current GPS
/// position (confirm tier: it changes HA's configuration). Both only ever read the owner's own person entity
/// (<see cref="NotificationTargets.Person"/>). Zone names are the owner's own HA configuration, so the output is
/// trusted.
/// </summary>
internal static class PlacesTools
{
    public const int DefaultRadius = 150;
    public const int MinRadius = 50;
    public const int MaxRadius = 1000;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public static IReadOnlyList<ITool> Create(IHomeAssistantCache cache, IHomeAssistantCommands commands, IUserPreferencesStore preferences) =>
    [
        new FindTool(cache, preferences),
        new CreateHereTool(cache, commands, preferences),
    ];

    private sealed record Zone(string Id, string Name, int Radius, bool Passive, bool? OwnerInside, string? PersonState);

    private static ToolDescriptor Descriptor(string name, string description, string schema, RiskTier tier) => new()
    {
        Id = new ToolId("places", name),
        Description = description,
        ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
        DefaultTier = tier,
    };

    private static async Task<string?> PersonAsync(IUserPreferencesStore preferences, OwnerId owner, CancellationToken cancellationToken) =>
        (await preferences.GetAsync(owner, cancellationToken)).NotificationTargets.Person;

    private static IEnumerable<Zone> Zones(HaSnapshot snapshot, string? person)
    {
        string? personState = person is null ? null : snapshot.States.GetValueOrDefault(person)?.State;
        return snapshot.States.Values
            .Where(state => state.EntityId.StartsWith("zone.", StringComparison.Ordinal))
            .OrderBy(state => state.EntityId, StringComparer.Ordinal)
            .Select(state => ToZone(state.EntityId, state.FriendlyName ?? state.EntityId["zone.".Length..], state.Attributes, person is null ? null : personState));
    }

    // A person entity takes "home" in zone.home and the zone's name elsewhere; never a passive zone's name.
    private static Zone ToZone(string id, string name, JsonElement attributes, string? ownerState)
    {
        bool passive = Bool(attributes, "passive");
        string? personState = passive ? null : id == "zone.home" ? "home" : name;
        int radius = attributes.ValueKind == JsonValueKind.Object && attributes.TryGetProperty("radius", out JsonElement value) && value.TryGetDouble(out double meters)
            ? (int)Math.Round(meters)
            : 0;
        bool? inside = ownerState is null ? null : personState is not null && string.Equals(personState, ownerState, StringComparison.Ordinal);
        return new Zone(id, name, radius, passive, inside, personState);
    }

    private static bool Bool(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.True;

    private static double? Number(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.Number ? value.GetDouble() : null;

    private static string? Text(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static Error Unavailable() => new("ha.unavailable", "Home Assistant is not connected.", ErrorType.Unavailable);

    private static Error Invalid(string message) => new("places.invalid_arguments", message, ErrorType.Validation);

    private sealed class FindTool(IHomeAssistantCache cache, IUserPreferencesStore preferences) : ITool
    {
        public ToolDescriptor Descriptor { get; } = PlacesTools.Descriptor("find",
            "Finds Home Assistant zones (places) by name. Each zone has personState: the value the user's person entity takes there. For a location reminder, call tasks_watch with filter {source: ha, entity: <person>, to: <personState>} (arriving) or from (leaving).",
            """
            {"type":"object","properties":{"query":{"type":"string","description":"Part of the place name, e.g. 'hornbach'. All zones when omitted."}}}
            """, RiskTier.Safe);

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            if (cache.Current is not { } snapshot)
                return Unavailable();

            string? query = Text(invocation.Arguments, "query")?.Trim();
            string? person = await PersonAsync(preferences, invocation.Context.OwnerId, cancellationToken);
            Zone[] zones = [.. Zones(snapshot, person).Where(zone => string.IsNullOrEmpty(query)
                || zone.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || zone.Id.Contains(query, StringComparison.OrdinalIgnoreCase))];
            return new ToolOutput(JsonSerializer.Serialize(new { person, zones }, _json));
        }
    }

    private sealed class CreateHereTool(IHomeAssistantCache cache, IHomeAssistantCommands commands, IUserPreferencesStore preferences) : ITool
    {
        public ToolDescriptor Descriptor { get; } = PlacesTools.Descriptor("create_here",
            "Creates a Home Assistant zone at the user's current position, for a place that has no zone yet. The user confirms it first.",
            $$$"""
            {"type":"object","properties":{
              "name":{"type":"string","description":"Zone name, e.g. 'Hornbach Linz'."},
              "radius":{"type":"integer","minimum":{{{MinRadius}}},"maximum":{{{MaxRadius}}},"description":"Radius in meters (default {{{DefaultRadius}}})."}},
             "required":["name"]}
            """, RiskTier.Confirm);

        public async ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
            await PersonAsync(preferences, context.OwnerId, cancellationToken) is not null;

        public Result Validate(JsonElement arguments) => Parse(arguments) is { IsFailure: true } parsed ? parsed.Error : Result.Success();

        public string Summarize(JsonElement arguments, string language) =>
            Parse(arguments) is { IsSuccess: true } parsed
                ? $"Create zone \"{parsed.Value.Name}\" ({parsed.Value.Radius.ToString(CultureInfo.InvariantCulture)} m) at your current position"
                : $"{Descriptor.Id} {arguments.GetRawText()}";

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result<(string Name, int Radius)> parsed = Parse(invocation.Arguments);
            if (parsed.IsFailure)
                return parsed.Error;
            (string name, int radius) = parsed.Value;

            if (await PersonAsync(preferences, invocation.Context.OwnerId, cancellationToken) is not { } person)
                return new Error("places.no_person", "No person entity is configured for the user.", ErrorType.Validation);
            if (cache.Current is not { } snapshot)
                return Unavailable();

            // The position is read now, at confirmation time, not when the model asked.
            JsonElement attributes = snapshot.States.GetValueOrDefault(person)?.Attributes ?? default;
            if (Number(attributes, "latitude") is not { } latitude || Number(attributes, "longitude") is not { } longitude)
                return new Error("places.position_unknown", "The user's current position is unknown (the person entity has no GPS position).", ErrorType.Failure);
            if (Zones(snapshot, person: null).FirstOrDefault(zone => string.Equals(zone.Name, name, StringComparison.OrdinalIgnoreCase)) is { } existing)
                return new Error("places.zone_exists", $"A zone named '{existing.Name}' already exists ({existing.Id}).", ErrorType.Conflict);

            Result<JsonElement> created = await commands.CommandAsync("zone/create", new JsonObject
            {
                ["name"] = name,
                ["latitude"] = latitude,
                ["longitude"] = longitude,
                ["radius"] = radius,
            }, cancellationToken);
            if (created.IsFailure)
                return created.Error;

            JsonElement item = created.Value;
            string id = Text(item, "id") is { Length: > 0 } storageId ? $"zone.{storageId}" : $"zone.{name.ToLowerInvariant()}";
            Zone zone = ToZone(id, Text(item, "name") ?? name, item, ownerState: null);
            return new ToolOutput(JsonSerializer.Serialize(new { person, zone, latitude, longitude }, _json));
        }

        private static Result<(string Name, int Radius)> Parse(JsonElement arguments)
        {
            if (Text(arguments, "name")?.Trim() is not { Length: > 0 } name)
                return Invalid("name is required.");

            int radius = DefaultRadius;
            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("radius", out JsonElement value) && value.ValueKind != JsonValueKind.Null)
            {
                if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out radius) || radius is < MinRadius or > MaxRadius)
                    return Invalid($"radius must be a whole number of meters between {MinRadius} and {MaxRadius}.");
            }

            return (name, radius);
        }
    }
}
