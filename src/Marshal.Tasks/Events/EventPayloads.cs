using System.Text.Encodings.Web;
using System.Text.Json;
using Marshal.Abstractions.Events;

namespace Marshal.Tasks.Events;

/// <summary>
/// The trigger payload a watch's run receives: the normalized event as JSON.
/// </summary>
internal static class EventPayloads
{
    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public static string Serialize(MarshalEvent @event) =>
        JsonSerializer.Serialize(new { source = @event.Source, type = @event.Type, entity = @event.Entity, time = @event.Time, payload = @event.Payload }, _json);
}
