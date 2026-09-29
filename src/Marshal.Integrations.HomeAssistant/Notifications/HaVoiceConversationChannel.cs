using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.FollowUps;
using Marshal.Abstractions.Notifications;

namespace Marshal.Integrations.HomeAssistant.Notifications;

/// <summary>
/// Speaks a notification that expects a reply: stores a follow-up record and calls
/// <c>assist_satellite.start_conversation</c> with <c>marshal-followup:&lt;nonce&gt;</c> as <c>extra_system_prompt</c>,
/// which comes back with the reply turn.
/// </summary>
public sealed class HaVoiceConversationChannel(IHomeAssistantClient client, IFollowUpStore followUps) : INotificationChannel
{
    public NotificationChannelKind Kind => NotificationChannelKind.VoiceConversation;

    public async Task<Result> DeliverAsync(ChannelMessage message, CancellationToken cancellationToken)
    {
        if (message.Satellite is not { } satellite)
            return new Error("notifications.no_satellite", "No satellite to speak on.", ErrorType.Validation);

        FollowUpRecord followUp = await followUps.CreateAsync(new NewFollowUp(satellite, message.TaskId, message.PendingActionId), cancellationToken);
        Result<JsonElement> called = await client.CallServiceAsync("assist_satellite", "start_conversation", new JsonObject
        {
            ["entity_id"] = satellite,
            ["start_message"] = message.Text,
            ["extra_system_prompt"] = followUp.Prompt,
        }, cancellationToken);
        return called.IsSuccess ? Result.Success() : called.Error;
    }
}
