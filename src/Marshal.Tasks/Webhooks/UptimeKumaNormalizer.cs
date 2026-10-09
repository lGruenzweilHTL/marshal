using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;

namespace Marshal.Tasks.Webhooks;

/// <summary>
/// Uptime Kuma's webhook notification (<c>{ heartbeat, monitor, msg }</c> as JSON, or that JSON in the form field
/// <c>data</c>). Heartbeats become <c>monitor.down|up|pending|maintenance</c>, certificate notices
/// <c>certificate.expiring</c>; test and other notices publish nothing. Trusted: payloads hold only the owner's own
/// monitor configuration and numbers, never Uptime Kuma's free-text messages (<c>msg</c>, <c>heartbeat.msg</c>),
/// which carry remote error text.
/// </summary>
public sealed partial class UptimeKumaNormalizer : IWebhookNormalizer
{
    public const string SourceName = "uptime-kuma";

    private static readonly string[] _statuses = ["monitor.down", "monitor.up", "monitor.pending", "monitor.maintenance"];

    public string Source => SourceName;

    public bool Trusted => true;

    public Result<IReadOnlyList<WebhookEvent>> Normalize(WebhookRequest request)
    {
        JsonObject body;
        try
        {
            JsonNode? node = request.Form.TryGetValue("data", out string? data) ? JsonNode.Parse(data) : JsonNode.Parse(request.Body.Span);
            if (node is not JsonObject root)
                return WebhookErrors.InvalidBody("expected a JSON object");
            body = root;
        }
        catch (JsonException)
        {
            return WebhookErrors.InvalidBody("not valid JSON");
        }

        var monitor = body["monitor"] as JsonObject;
        if (body["heartbeat"] is JsonObject heartbeat)
            return Heartbeat(heartbeat, monitor);
        if (body["msg"] is JsonValue msg && msg.TryGetValue(out string? text) && CertificateNotice().Match(text) is { Success: true } match)
            return Certificate(Name(monitor) ?? match.Groups["name"].Value, int.Parse(match.Groups["days"].Value, CultureInfo.InvariantCulture));
        return Array.Empty<WebhookEvent>();
    }

    private static Result<IReadOnlyList<WebhookEvent>> Heartbeat(JsonObject heartbeat, JsonObject? monitor)
    {
        if (monitor is null || Name(monitor) is not { } name)
            return WebhookErrors.InvalidBody("a heartbeat without monitor");
        if (heartbeat["status"] is not JsonValue statusValue || !statusValue.TryGetValue(out int status) || status is < 0 or > 3)
            return WebhookErrors.InvalidBody("unknown heartbeat status");

        var payload = new JsonObject
        {
            ["monitorId"] = Number(monitor["id"]),
            ["name"] = name,
            ["type"] = Text(monitor["type"]),
        };
        if (Text(monitor["url"]) is { } url && url is not ("" or "http://" or "https://"))
            payload["url"] = url;
        payload["time"] = Time(heartbeat["time"]);
        payload["pingMs"] = Number(heartbeat["ping"]);
        payload["important"] = heartbeat["important"] is JsonValue important && important.TryGetValue(out bool value) && value;
        return new WebhookEvent[] { new(_statuses[status], name, ToElement(payload)) };
    }

    private static Result<IReadOnlyList<WebhookEvent>> Certificate(string name, int days) =>
        new WebhookEvent[] { new("certificate.expiring", name, ToElement(new JsonObject { ["name"] = name, ["days"] = days })) };

    private static string? Name(JsonObject? monitor) => Text(monitor?["name"]) is { Length: > 0 } name ? name : null;

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private static int? Number(JsonNode? node) => node is JsonValue value && value.TryGetValue(out double number) ? (int)number : null;

    // Uptime Kuma writes heartbeat times as UTC "yyyy-MM-dd HH:mm:ss.fff" without an offset.
    private static DateTimeOffset? Time(JsonNode? node) =>
        Text(node) is { } text && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out DateTimeOffset time)
            ? time
            : null;

    private static JsonElement ToElement(JsonObject payload) => JsonSerializer.SerializeToElement(payload);

    // "[<monitor name>][<url>] <type> certificate <CN> will expire in <n> days" (English, not localized by Uptime Kuma).
    [GeneratedRegex(@"^\[(?<name>[^\]]+)\]\[[^\]]*\].*\bcertificate\b.*\bwill expire in (?<days>\d+) days?\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CertificateNotice();
}
