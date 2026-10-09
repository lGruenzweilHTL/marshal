using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;

namespace Marshal.Tasks.Webhooks;

/// <summary>
/// Proxmox VE's webhook notification target (PVE 8.3+), with the body template documented in the example config:
/// <c>{"severity":"{{ severity }}","timestamp":{{ timestamp }},"fields":{{ json fields }}}</c>. Backup (<c>vzdump</c>),
/// replication, fencing and package-update notifications become events; forwarded system mail and anything else
/// publish nothing. Trusted: the normalizer reads only severity, time and the metadata fields <c>type</c>,
/// <c>hostname</c> and <c>job-id</c>; title and message (which carry log text) are never copied.
/// </summary>
public sealed class ProxmoxNormalizer : IWebhookNormalizer
{
    public const string SourceName = "proxmox";

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

        if (body["fields"] is not JsonObject fields)
            return WebhookErrors.InvalidBody("expected an object 'fields' (see the body template in the example config)");

        string severity = Text(body["severity"]) ?? "unknown";
        string? type = Text(fields["type"]);
        string? eventType = type switch
        {
            "vzdump" => severity == "error" ? "backup.failed" : "backup.ok",
            "replication" => "replication.failed",
            "fencing" => "node.fenced",
            "package-updates" => "updates.available",
            _ => null,
        };
        if (eventType is null)
            return Array.Empty<WebhookEvent>();

        string? hostname = Text(fields["hostname"]);
        string? jobId = Text(fields["job-id"]);
        var payload = new JsonObject
        {
            ["type"] = type,
            ["severity"] = severity,
            ["hostname"] = hostname,
            ["jobId"] = jobId,
            ["time"] = body["timestamp"] is JsonValue timestamp && timestamp.TryGetValue(out long seconds) ? DateTimeOffset.FromUnixTimeSeconds(seconds) : null,
        };
        return new WebhookEvent[] { new(eventType, jobId ?? hostname, JsonSerializer.SerializeToElement(payload)) };
    }

    private static string? Text(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) && text.Length > 0 ? text : null;
}
