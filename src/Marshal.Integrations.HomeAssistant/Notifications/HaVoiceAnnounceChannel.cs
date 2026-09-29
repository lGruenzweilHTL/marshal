using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Notifications;

namespace Marshal.Integrations.HomeAssistant.Notifications;

/// <summary>
/// Speaks a notification with <c>assist_satellite.announce</c> (the satellite's pipeline does the TTS).
/// </summary>
public sealed class HaVoiceAnnounceChannel(IHomeAssistantClient client) : INotificationChannel
{
    public NotificationChannelKind Kind => NotificationChannelKind.VoiceAnnounce;

    public async Task<Result> DeliverAsync(ChannelMessage message, CancellationToken cancellationToken)
    {
        if (message.Satellite is not { } satellite)
            return new Error("notifications.no_satellite", "No satellite to speak on.", ErrorType.Validation);

        Result<System.Text.Json.JsonElement> called = await client.CallServiceAsync("assist_satellite", "announce", new JsonObject
        {
            ["entity_id"] = satellite,
            ["message"] = message.Text,
        }, cancellationToken);
        return called.IsSuccess ? Result.Success() : called.Error;
    }
}
