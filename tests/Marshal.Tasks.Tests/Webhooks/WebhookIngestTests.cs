using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Tasks.Webhooks;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Webhooks;

public sealed class WebhookIngestTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 10, 15, 0, TimeSpan.Zero));
    private readonly FakeEventPublisher _publisher = new();
    private readonly WebhookIngest _ingest;

    public WebhookIngestTests() =>
        _ingest = new WebhookIngest([new UptimeKumaNormalizer()], _publisher, _time, Options.Create(new WebhookOptions { DebounceSeconds = 60 }), NullLogger<WebhookIngest>.Instance);

    [Fact]
    public void Ingest_PublishesNormalizedEventsWithOwnerSourceTimeAndTrust()
    {
        Result<int> result = _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("down"));

        Assert.Equal(1, result.Value);
        MarshalEvent @event = Assert.Single(_publisher.Events);
        Assert.Equal(("uptime-kuma", "monitor.down", "NAS", (Abstractions.Identity.OwnerId?)OwnerA, _time.GetUtcNow(), true),
            (@event.Source, @event.Type, @event.Entity, @event.OwnerId, @event.Time, @event.Trusted));
        Assert.Equal("NAS", @event.Payload.GetProperty("name").GetString());
    }

    [Fact]
    public void Ingest_UnknownSource_IsNotFound()
    {
        Result<int> result = _ingest.Ingest("proxmox", OwnerA, KumaFixtures.Json("down"));

        Assert.Equal(("webhook.unknown_source", ErrorType.NotFound), (result.Error.Code, result.Error.Type));
        Assert.Empty(_publisher.Events);
    }

    [Fact]
    public void Ingest_InvalidBody_FailsAndPublishesNothing()
    {
        Assert.Equal("webhook.invalid_body", _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("invalid")).Error.Code);
        Assert.Empty(_publisher.Events);
    }

    [Fact]
    public void Ingest_RepeatWithinWindow_IsDropped()
    {
        _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("down"));
        _time.Advance(TimeSpan.FromSeconds(30));

        Result<int> repeat = _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("down"));

        Assert.Equal(0, repeat.Value);
        Assert.Single(_publisher.Events);
    }

    [Fact]
    public void Ingest_SameEventForAnotherOwner_IsPublished()
    {
        _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("down"));
        _ingest.Ingest("uptime-kuma", OwnerB, KumaFixtures.Json("down"));

        Assert.Equal(2, _publisher.Events.Count);
    }

    [Fact]
    public void Ingest_DifferentTypeWithinWindow_IsPublished()
    {
        _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("down"));
        _time.Advance(TimeSpan.FromSeconds(10));
        _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("pending"));

        Assert.Equal(["monitor.down", "monitor.pending"], _publisher.Events.Select(@event => @event.Type));
    }

    [Fact]
    public void Ingest_AfterTheWindow_IsPublishedAgain()
    {
        _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("down"));
        _time.Advance(TimeSpan.FromSeconds(61));
        _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("down"));

        Assert.Equal(2, _publisher.Events.Count);
    }

    [Fact]
    public void Ingest_TestNotification_PublishesNothingButCountsAsReceived()
    {
        IEventSource source = Assert.Single(_ingest.EventSources);
        Assert.Equal(("uptime-kuma", false, (DateTimeOffset?)null), (source.Source, source.IsConnected, source.LastReceivedAt));

        Assert.Equal(0, _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("test")).Value);

        Assert.Empty(_publisher.Events);
        Assert.Equal((true, (DateTimeOffset?)_time.GetUtcNow()), (source.IsConnected, source.LastReceivedAt));
        _time.Advance(TimeSpan.FromHours(25));
        Assert.False(source.IsConnected);
    }

    [Fact]
    public void Sources_ListTheNormalizers() =>
        Assert.Equal(["uptime-kuma"], _ingest.Sources);

    [Fact]
    public void EventPayload_OfAnIngestedEvent_SerializesAsTrusted()
    {
        _ingest.Ingest("uptime-kuma", OwnerA, KumaFixtures.Json("down"));

        string payload = Marshal.Tasks.Events.EventPayloads.Serialize(Assert.Single(_publisher.Events));

        Assert.True(JsonDocument.Parse(payload).RootElement.GetProperty("trusted").GetBoolean());
        Assert.True(Marshal.Tasks.Events.EventPayloads.IsTrusted(payload));
    }
}
