using System.Text.Encodings.Web;
using System.Text.Json;
using Marshal.Abstractions.Events;

namespace Marshal.Tasks.Events;

/// <summary>
/// The trigger payload a watch's run receives: the normalized event as JSON. A trusted event carries a top-level
/// <c>trusted: true</c>; the event's own data stays nested under <c>payload</c>, so content cannot set that key.
/// </summary>
internal static class EventPayloads
{
    public const string TrustedKey = "trusted";

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Serialize(MarshalEvent @event) => @event.Trusted
        ? JsonSerializer.Serialize(new { source = @event.Source, type = @event.Type, entity = @event.Entity, time = @event.Time, payload = @event.Payload, trusted = true }, _json)
        : JsonSerializer.Serialize(new { source = @event.Source, type = @event.Type, entity = @event.Entity, time = @event.Time, payload = @event.Payload }, _json);

    /// <summary>Whether a serialized event payload is from a trusted event (top-level <c>trusted: true</c>).</summary>
    public static bool IsTrusted(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(TrustedKey, out JsonElement trusted)
                && trusted.ValueKind == JsonValueKind.True;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
