using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Media;

namespace Marshal.Tasks.Webhooks;

/// <summary>
/// Seerr's webhook agent, with the JSON payload template documented in the example config: <c>notification_type</c>,
/// a <c>media</c> object (<c>media_type</c>, <c>tmdbId</c>, <c>status</c>, <c>jellyfinMediaId</c>) and a
/// <c>request</c> object (<c>request_id</c>). Approved, available, declined and failed requests become events; anything
/// else publishes nothing. Trusted: only ids and the notification type are read; subject, message and image (TMDB text
/// and URLs) are never copied.
/// </summary>
public sealed class SeerrNormalizer(IJellyfinLinks links) : IWebhookNormalizer
{
    public const string SourceName = "seerr";

    public string Source => SourceName;

    public bool Trusted => true;

    public Result<IReadOnlyList<WebhookEvent>> Normalize(WebhookRequest request)
    {
        JsonObject body;
        try
        {
            if (JsonNode.Parse(request.Body.Span) is not JsonObject root)
                return WebhookErrors.InvalidBody("expected a JSON object");
            body = root;
        }
        catch (JsonException)
        {
            return WebhookErrors.InvalidBody("not valid JSON");
        }

        string? type = Text(body["notification_type"]) switch
        {
            "MEDIA_AVAILABLE" => "media.available",
            "MEDIA_APPROVED" or "MEDIA_AUTO_APPROVED" => "media.approved",
            "MEDIA_DECLINED" => "media.declined",
            "MEDIA_FAILED" => "media.failed",
            _ => null,
        };
        if (type is null || body["media"] is not JsonObject media)
            return Array.Empty<WebhookEvent>();

        if (Number(media["tmdbId"]) is not { } tmdbId)
            return WebhookErrors.InvalidBody("expected 'media.tmdbId' (see the payload template in the example config)");
        string? mediaType = Text(media["media_type"]);
        if (mediaType is not ("movie" or "tv"))
            return WebhookErrors.InvalidBody("expected 'media.media_type' movie or tv");

        string? itemId = Text(media["jellyfinMediaId"]);
        var payload = new JsonObject
        {
            ["mediaType"] = mediaType,
            ["tmdbId"] = tmdbId,
            ["requestId"] = body["request"] is JsonObject requestInfo ? Number(requestInfo["request_id"]) : null,
            ["jellyfinItemId"] = itemId,
            ["jellyfin"] = links.ForItem(itemId),
        };
        return new WebhookEvent[] { new(type, $"{mediaType}:{tmdbId.ToString(CultureInfo.InvariantCulture)}", JsonSerializer.SerializeToElement(payload)) };
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0 ? text : null;

    // Template values arrive as strings ("438631"); a hand-written template may send numbers.
    private static int? Number(JsonNode? node) =>
        node is JsonValue value && (value.TryGetValue(out int number) || (value.TryGetValue(out string? text) && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out number)))
            ? number
            : null;
}

/// <summary>Jellyfin links while the media integration is not registered (e.g. in tests): no links.</summary>
internal sealed class NoJellyfinLinks : IJellyfinLinks
{
    public string? ForItem(string? itemId) => null;
}
