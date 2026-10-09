using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Webhooks;

/// <summary>
/// The webhook ingest: one <see cref="IWebhookNormalizer"/> per source, an in-memory debounce (lost on restart, so at
/// most one duplicate after a restart) and one <see cref="IEventSource"/> per source for the system view.
/// </summary>
public sealed partial class WebhookIngest : IWebhookIngest
{
    private static readonly TimeSpan ConnectedFor = TimeSpan.FromHours(24);

    private readonly Dictionary<string, (IWebhookNormalizer Normalizer, SourceState State)> _sources;
    private readonly IEventPublisher _publisher;
    private readonly TimeProvider _time;
    private readonly TimeSpan _window;
    private readonly Dictionary<(OwnerId Owner, string Source, string Type, string? Entity), DateTimeOffset> _published = [];
    private readonly Lock _lock = new();
    private readonly ILogger<WebhookIngest> _logger;

    public WebhookIngest(IEnumerable<IWebhookNormalizer> normalizers, IEventPublisher publisher, TimeProvider time, IOptions<WebhookOptions> options, ILogger<WebhookIngest> logger)
    {
        _logger = logger;
        _sources = normalizers.ToDictionary(normalizer => normalizer.Source, normalizer => (normalizer, new SourceState(normalizer.Source, time)), StringComparer.Ordinal);
        _publisher = publisher;
        _time = time;
        _window = TimeSpan.FromSeconds(options.Value.DebounceSeconds);
    }

    public IReadOnlyList<string> Sources => [.. _sources.Keys.Order(StringComparer.Ordinal)];

    /// <summary>One event source per webhook source, connected while a webhook arrived in the last 24 hours.</summary>
    public IReadOnlyList<IEventSource> EventSources => [.. _sources.Values.Select(source => source.State).OrderBy(state => state.Source, StringComparer.Ordinal)];

    public Result<int> Ingest(string source, OwnerId owner, WebhookRequest request)
    {
        if (!_sources.TryGetValue(source, out (IWebhookNormalizer Normalizer, SourceState State) entry))
            return WebhookErrors.UnknownSource(source);

        Result<IReadOnlyList<WebhookEvent>> normalized = entry.Normalizer.Normalize(request);
        if (normalized.IsFailure)
            return normalized.Error;

        DateTimeOffset now = _time.GetUtcNow();
        entry.State.Received(now);
        if (normalized.Value.Count == 0)
            LogNoEvent(source);

        int published = 0;
        foreach (WebhookEvent @event in normalized.Value)
        {
            if (!TryClaim((owner, source, @event.Type, @event.Entity), now))
                continue;
            _publisher.Publish(new MarshalEvent(source, @event.Type, @event.Entity, @event.Payload, owner, now, entry.Normalizer.Trusted));
            published++;
        }

        return published;
    }

    // Test notifications and notices the normalizer does not know (e.g. changed wording) end here.
    [LoggerMessage(Level = LogLevel.Debug, Message = "A {Source} webhook contained no event (test or unknown notice).")]
    private partial void LogNoEvent(string source);

    private bool TryClaim((OwnerId, string, string, string?) key, DateTimeOffset now)
    {
        lock (_lock)
        {
            foreach ((OwnerId, string, string, string?) stale in _published.Where(pair => now - pair.Value >= _window).Select(pair => pair.Key).ToList())
                _published.Remove(stale);
            if (_published.ContainsKey(key))
                return false;
            _published[key] = now;
            return true;
        }
    }

    private sealed class SourceState(string source, TimeProvider time) : IEventSource
    {
        private long _lastTicks = -1;

        public string Source => source;

        public bool IsConnected => LastReceivedAt is { } last && time.GetUtcNow() - last < ConnectedFor;

        public DateTimeOffset? LastReceivedAt
        {
            get
            {
                long ticks = Interlocked.Read(ref _lastTicks);
                return ticks < 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
            }
        }

        public void Received(DateTimeOffset at) => Interlocked.Exchange(ref _lastTicks, at.UtcTicks);
    }
}
