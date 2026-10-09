namespace Marshal.Abstractions.Events;

/// <summary>
/// Turns one webhook source's request bodies into events. One per source, defined in code; pure (no I/O). Its
/// <see cref="Source"/> is the route value of <c>POST /api/v1/hooks/{source}</c> and the suffix of the token scope
/// <c>hooks:&lt;source&gt;</c>.
/// </summary>
public interface IWebhookNormalizer
{
    /// <summary>Source name, e.g. <c>uptime-kuma</c>; also the <see cref="MarshalEvent.Source"/> of its events.</summary>
    string Source { get; }

    /// <summary>Whether the source's events are free of third-party text, so their runs are not tainted.</summary>
    bool Trusted { get; }

    /// <summary>The events in the request (none for test or unknown notices), or <c>webhook.invalid_body</c>.</summary>
    Result<IReadOnlyList<WebhookEvent>> Normalize(WebhookRequest request);
}

/// <summary>
/// Receives webhooks: normalizes them with the source's <see cref="IWebhookNormalizer"/>, drops repeats within the
/// debounce window and publishes the rest as events owned by the token's owner.
/// </summary>
public interface IWebhookIngest
{
    /// <summary>The registered source names.</summary>
    IReadOnlyList<string> Sources { get; }

    /// <summary>The number of events published, or <c>webhook.unknown_source</c> / <c>webhook.invalid_body</c>.</summary>
    Result<int> Ingest(string source, Identity.OwnerId owner, WebhookRequest request);
}

/// <summary>A received webhook request.</summary>
/// <param name="ContentType">The request's content type, null when not sent.</param>
/// <param name="Body">The raw body; empty for form bodies, whose fields are in <paramref name="Form"/>.</param>
/// <param name="Form">Fields of a form body (<c>multipart/form-data</c> or URL-encoded); empty otherwise.</param>
public sealed record WebhookRequest(string? ContentType, ReadOnlyMemory<byte> Body, IReadOnlyDictionary<string, string> Form);

/// <summary>One normalized event; the ingest adds source, owner, time and trust.</summary>
/// <param name="Type">Event type, e.g. <c>monitor.down</c>.</param>
/// <param name="Entity">What it is about, e.g. the monitor name.</param>
/// <param name="Payload">Event data (a JSON object).</param>
public sealed record WebhookEvent(string Type, string? Entity, System.Text.Json.JsonElement Payload);

/// <summary>Expected failures of the webhook ingest.</summary>
public static class WebhookErrors
{
    public static Error InvalidBody(string reason) => new("webhook.invalid_body", $"The webhook body could not be read: {reason}", ErrorType.Validation);

    public static Error UnknownSource(string source) => new("webhook.unknown_source", $"There is no webhook source '{source}'.", ErrorType.NotFound);
}
