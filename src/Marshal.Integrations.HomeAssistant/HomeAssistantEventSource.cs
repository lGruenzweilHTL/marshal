using System.Text.Json;
using Marshal.Abstractions.Events;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Publishes HA events from the cache's websocket subscription: <c>state_changed</c> when an entity's state (not
/// just its attributes) changes, with <c>from</c>, <c>to</c> and the new attributes, and
/// <c>mobile_app_notification_action</c> (companion app push actions) with the event data. HA events are
/// instance-wide, so they carry no owner.
/// </summary>
public sealed class HomeAssistantEventSource(IEventPublisher publisher, TimeProvider time) : IEventSource
{
    public const string Name = "ha";
    public const string StateChanged = "state_changed";
    public const string NotificationAction = "mobile_app_notification_action";

    private volatile bool _connected;

    public string Source => Name;

    public bool IsConnected => _connected;

    /// <summary>The HA event types the cache subscribes to for this source.</summary>
    internal static IReadOnlyList<string> EventTypes { get; } = [StateChanged, NotificationAction];

    internal void SetConnected(bool connected) => _connected = connected;

    /// <summary>Handles the <c>event</c> object of an HA event message.</summary>
    internal void Handle(JsonElement haEvent)
    {
        if (!haEvent.TryGetProperty("event_type", out JsonElement typeElement) || !haEvent.TryGetProperty("data", out JsonElement data) || data.ValueKind != JsonValueKind.Object)
            return;

        switch (typeElement.GetString())
        {
            case StateChanged:
                PublishStateChanged(data);
                break;
            case NotificationAction:
                publisher.Publish(new MarshalEvent(Name, NotificationAction, null, data.Clone(), null, time.GetUtcNow()));
                break;
        }
    }

    private void PublishStateChanged(JsonElement data)
    {
        if (!data.TryGetProperty("entity_id", out JsonElement entity) || entity.GetString() is not { } entityId)
            return;

        string? from = State(data, "old_state", out _);
        string? to = State(data, "new_state", out JsonElement attributes);
        if (from == to)
            return;

        JsonElement payload = JsonSerializer.SerializeToElement(new
        {
            from,
            to,
            attributes = attributes.ValueKind == JsonValueKind.Object ? attributes : JsonSerializer.SerializeToElement(new { }),
        });
        publisher.Publish(new MarshalEvent(Name, StateChanged, entityId, payload, null, time.GetUtcNow()));
    }

    private static string? State(JsonElement data, string name, out JsonElement attributes)
    {
        attributes = default;
        if (!data.TryGetProperty(name, out JsonElement state) || state.ValueKind != JsonValueKind.Object)
            return null;
        if (state.TryGetProperty("attributes", out JsonElement found))
            attributes = found;
        return state.TryGetProperty("state", out JsonElement value) ? value.GetString() : null;
    }
}
