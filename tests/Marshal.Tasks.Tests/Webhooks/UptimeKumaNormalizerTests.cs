using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Tasks.Webhooks;

namespace Marshal.Tasks.Tests.Webhooks;

public sealed class UptimeKumaNormalizerTests
{
    private readonly UptimeKumaNormalizer _normalizer = new();

    [Fact]
    public void Source_IsUptimeKumaAndTrusted() =>
        Assert.Equal(("uptime-kuma", true), (_normalizer.Source, _normalizer.Trusted));

    [Theory]
    [InlineData("down", "monitor.down", "NAS")]
    [InlineData("up", "monitor.up", "Nextcloud")]
    [InlineData("pending", "monitor.pending", "NAS")]
    [InlineData("maintenance", "monitor.maintenance", "Nextcloud")]
    public void Heartbeat_MapsStatusToTypeAndMonitorNameToEntity(string fixture, string type, string entity)
    {
        WebhookEvent @event = Assert.Single(Normalize(KumaFixtures.Json(fixture)));

        Assert.Equal((type, entity), (@event.Type, @event.Entity));
    }

    [Fact]
    public void Heartbeat_PayloadHasMonitorAndHeartbeatFields()
    {
        JsonElement payload = Assert.Single(Normalize(KumaFixtures.Json("up"))).Payload;

        Assert.Equal(5, payload.GetProperty("monitorId").GetInt32());
        Assert.Equal("Nextcloud", payload.GetProperty("name").GetString());
        Assert.Equal("http", payload.GetProperty("type").GetString());
        Assert.Equal("https://cloud.example", payload.GetProperty("url").GetString());
        Assert.Equal(DateTimeOffset.Parse("2026-10-08T08:15:02.417Z", null, System.Globalization.DateTimeStyles.AssumeUniversal), payload.GetProperty("time").GetDateTimeOffset());
        Assert.Equal(42, payload.GetProperty("pingMs").GetInt32());
        Assert.True(payload.GetProperty("important").GetBoolean());
    }

    [Fact]
    public void Heartbeat_WithoutUrl_LeavesUrlAndPingOut()
    {
        JsonElement payload = Assert.Single(Normalize(KumaFixtures.Json("down"))).Payload;

        Assert.False(payload.TryGetProperty("url", out _));
        Assert.Equal(JsonValueKind.Null, payload.GetProperty("pingMs").ValueKind);
    }

    [Fact]
    public void FormData_ReadsTheDataField()
    {
        WebhookEvent @event = Assert.Single(Normalize(KumaFixtures.Form("down")));

        Assert.Equal(("monitor.down", "NAS"), (@event.Type, @event.Entity));
    }

    [Fact]
    public void CertificateNotice_BecomesCertificateExpiringWithDays()
    {
        WebhookEvent @event = Assert.Single(Normalize(KumaFixtures.Json("certificate")));

        Assert.Equal(("certificate.expiring", "Nextcloud"), (@event.Type, @event.Entity));
        Assert.Equal(14, @event.Payload.GetProperty("days").GetInt32());
        Assert.Equal("Nextcloud", @event.Payload.GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("test")]
    [InlineData("unknown")]
    public void TestAndUnknownNotices_PublishNothing(string fixture) =>
        Assert.Empty(Normalize(KumaFixtures.Json(fixture)));

    [Fact]
    public void InvalidJson_Fails()
    {
        Result<IReadOnlyList<WebhookEvent>> result = _normalizer.Normalize(KumaFixtures.Json("invalid"));

        Assert.True(result.IsFailure);
        Assert.Equal("webhook.invalid_body", result.Error.Code);
    }

    [Fact]
    public void NonObjectBodyAndUnknownStatus_Fail()
    {
        Assert.True(_normalizer.Normalize(Request("[1, 2]")).IsFailure);
        Assert.True(_normalizer.Normalize(Request("""{"heartbeat":{"status":9},"monitor":{"id":1,"name":"x","type":"http"}}""")).IsFailure);
    }

    [Fact]
    public void NoPayload_ContainsMessageText()
    {
        foreach (string fixture in KumaFixtures.Names.Where(name => name != "invalid"))
        {
            foreach (WebhookEvent @event in Normalize(KumaFixtures.Json(fixture)))
            {
                string payload = @event.Payload.GetRawText();
                Assert.DoesNotContain("msg", payload, StringComparison.Ordinal);
                Assert.DoesNotContain("ECONNREFUSED", payload, StringComparison.Ordinal);
                Assert.DoesNotContain("ignore previous", payload, StringComparison.Ordinal);
                Assert.DoesNotContain("200 - OK", payload, StringComparison.Ordinal);
                Assert.DoesNotContain("certificate", payload, StringComparison.Ordinal);
            }
        }
    }

    private IReadOnlyList<WebhookEvent> Normalize(WebhookRequest request)
    {
        Result<IReadOnlyList<WebhookEvent>> result = _normalizer.Normalize(request);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static WebhookRequest Request(string json) => new("application/json", Encoding.UTF8.GetBytes(json), new Dictionary<string, string>());
}
