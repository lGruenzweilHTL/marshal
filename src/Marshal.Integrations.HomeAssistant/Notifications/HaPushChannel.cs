using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;

namespace Marshal.Integrations.HomeAssistant.Notifications;

/// <summary>
/// Pushes to every companion app notify service of the owner (<c>notify.mobile_app_*</c>): the link opens on tap
/// (<c>url</c> on iOS, <c>clickAction</c> on Android), actions become buttons, a silent push is passive / low
/// importance and a critical one breaks through (iOS <c>critical</c>, Android alarm stream, high priority, no TTL).
/// Succeeds when at least one device got it.
/// </summary>
public sealed class HaPushChannel(IHomeAssistantClient client, IUserPreferencesStore preferences) : INotificationChannel
{
    private const string NotifyPrefix = "notify.";

    public NotificationChannelKind Kind => NotificationChannelKind.Push;

    public async Task<Result> DeliverAsync(ChannelMessage message, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> targets = (await preferences.GetAsync(message.Owner, cancellationToken)).NotificationTargets.Push;
        Error? failure = new("notifications.no_push_target", "No push target is configured.", ErrorType.Unavailable);
        bool delivered = false;
        foreach (string target in targets.Where(target => target.StartsWith(NotifyPrefix, StringComparison.Ordinal)))
        {
            Result<JsonElement> sent = await client.CallServiceAsync("notify", target[NotifyPrefix.Length..], Body(message), cancellationToken);
            if (sent.IsSuccess)
                delivered = true;
            else
                failure = sent.Error;
        }

        return delivered ? Result.Success() : failure;
    }

    private static JsonObject Body(ChannelMessage message)
    {
        var data = new JsonObject();
        if (message.Link is { } link)
        {
            data["url"] = link;
            data["clickAction"] = link;
        }

        if (message.Tag is { } tag)
            data["tag"] = tag;
        if (message.Actions is { Count: > 0 } actions)
            data["actions"] = new JsonArray([.. actions.Select(action => (JsonNode)new JsonObject { ["action"] = action.Id, ["title"] = action.Title })]);

        if (message.Priority == NotificationPriority.Critical)
        {
            data["push"] = new JsonObject { ["interruption-level"] = "critical" };
            data["channel"] = "alarm_stream";
            data["priority"] = "high";
            data["ttl"] = 0;
        }
        else if (message.Silent)
        {
            data["push"] = new JsonObject { ["interruption-level"] = "passive" };
            data["importance"] = "low";
        }

        return new JsonObject { ["message"] = message.Text, ["title"] = "Marshal", ["data"] = data };
    }
}
