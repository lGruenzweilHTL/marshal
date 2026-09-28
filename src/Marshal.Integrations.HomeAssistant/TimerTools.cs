using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// <c>timers.start</c>, <c>timers.cancel</c> and <c>timers.status</c>: HA's timer intents, called through HA's
/// intent handling with the turn's device, so the timer runs on (and rings at) that satellite. HA MCP cannot offer
/// them (it has no device context); should it ever offer <c>HassStartTimer</c>, these tools step aside. Hidden in
/// turns without a device.
/// </summary>
public static partial class TimerTools
{
    public static IReadOnlyList<ITool> Create(IHomeAssistantClient client) =>
    [
        new TimerTool(client, "start", "HassStartTimer", "Starts a timer on the device the user is talking to.", """
            {"type":"object","properties":{
              "duration":{"type":"string","description":"Timer length, e.g. '10m', '1h30m', '90s'."},
              "name":{"type":"string","description":"Optional timer name, e.g. 'pasta'."}},
             "required":["duration"]}
            """),
        new TimerTool(client, "cancel", "HassCancelTimer", "Cancels a timer on the device the user is talking to (by name, or the only one).", """
            {"type":"object","properties":{"name":{"type":"string","description":"Name of the timer to cancel."}}}
            """),
        new TimerTool(client, "status", "HassTimerStatus", "Lists the timers running on the device the user is talking to.", """
            {"type":"object","properties":{}}
            """),
    ];

    /// <summary>Parses '10m', '1h 30m', '90 seconds' or ISO 8601 'PT1H30M' into HA's hours / minutes / seconds slots.</summary>
    internal static Result<JsonObject> Duration(string text)
    {
        text = text.Trim();
        Match iso = IsoDuration().Match(text);
        IEnumerable<(string Value, char Unit)> parts = iso.Success
            ? new[] { (iso.Groups[1].Value, 'h'), (iso.Groups[2].Value, 'm'), (iso.Groups[3].Value, 's') }.Where(part => part.Item1.Length > 0)
            : Parts(text);

        var slots = new JsonObject();
        int total = 0;
        foreach ((string value, char unit) in parts)
        {
            int amount = int.Parse(value, CultureInfo.InvariantCulture);
            string slot = unit switch { 'h' => "hours", 'm' => "minutes", _ => "seconds" };
            slots[slot] = ((int?)slots[slot] ?? 0) + amount;
            total += amount;
        }

        return total > 0 ? slots : new Error("timers.invalid_duration", $"'{text}' is not a duration like '10m', '1h30m' or '90s'.", ErrorType.Validation);
    }

    private static List<(string Value, char Unit)> Parts(string text)
    {
        var parts = new List<(string, char)>();
        int position = 0;
        foreach (Match match in DurationPart().Matches(text))
        {
            if (match.Index != position)
                return [];

            parts.Add((match.Groups[1].Value, char.ToLowerInvariant(match.Groups[2].Value[0])));
            position = match.Index + match.Length;
        }

        return position == text.Length ? parts : [];
    }

    [GeneratedRegex(@"^PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex IsoDuration();

    [GeneratedRegex(@"\s*(\d+)\s*(hours?|hrs?|h|minutes?|mins?|m|seconds?|secs?|s)(?![a-z])\s*(?:and\s*)?", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DurationPart();

    private sealed class TimerTool(IHomeAssistantClient client, string name, string intent, string description, string schema) : ITool
    {
        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("timers", name),
            Description = description,
            ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
            DefaultTier = RiskTier.Safe,
        };

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) =>
            ValueTask.FromResult(context.DeviceId is not null && !HaOffersTimers(context));

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            JsonElement arguments = invocation.Arguments;
            var data = new JsonObject();
            if (intent == "HassStartTimer")
            {
                string duration = arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("duration", out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : "";
                Result<JsonObject> slots = Duration(duration);
                if (slots.IsFailure)
                    return slots.Error;

                data = slots.Value;
            }

            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("name", out JsonElement timerName) && timerName.ValueKind == JsonValueKind.String)
                data["name"] = timerName.GetString();

            MarshalExecutionContext context = invocation.Context;
            Result<JsonElement> response = await client.HandleIntentAsync(new HaIntentRequest(intent, data, context.Language, context.DeviceId, context.SatelliteId), cancellationToken);
            if (response.IsFailure)
                return response.Error;

            string? speech = Speech(response.Value);
            if (Text(response.Value, "response_type") == "error")
                return new Error("ha.intent_failed", speech ?? "Home Assistant could not handle the timer request.");

            return new ToolOutput(speech ?? Slots(response.Value));
        }

        private static bool HaOffersTimers(MarshalExecutionContext context) =>
            context.Tools?.Tools.Any(tool => tool.Id.Namespace == "ha" && tool.Id.Name.EndsWith("HassStartTimer", StringComparison.Ordinal)) == true;

        private static string? Speech(JsonElement response) =>
            response.TryGetProperty("speech", out JsonElement speech) && speech.ValueKind == JsonValueKind.Object
            && speech.TryGetProperty("plain", out JsonElement plain) && Text(plain, "speech") is { Length: > 0 } text
                ? text
                : null;

        private static string Slots(JsonElement response) =>
            response.TryGetProperty("speech_slots", out JsonElement slots) ? slots.GetRawText() : "done";

        private static string? Text(JsonElement element, string property) =>
            element.ValueKind == JsonValueKind.Object && element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
    }
}
