namespace Marshal.Tasks.Webhooks;

/// <summary>Webhook ingest limits (<c>webhooks</c> in <c>marshal.yaml</c>).</summary>
public sealed class WebhookOptions
{
    /// <summary>A repeat of an event (same owner, source, type and entity) within this many seconds is dropped.</summary>
    public int DebounceSeconds { get; set; } = 60;

    /// <summary>Requests per token and minute; more get 429.</summary>
    public int RequestsPerMinute { get; set; } = 60;
}
