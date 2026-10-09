using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Tasks.Webhooks;

namespace Marshal.Tasks.Tests.Webhooks;

public sealed class ProxmoxNormalizerTests
{
    private readonly ProxmoxNormalizer _normalizer = new();

    [Fact]
    public void Source_IsProxmoxAndTrusted() =>
        Assert.Equal(("proxmox", true), (_normalizer.Source, _normalizer.Trusted));

    [Theory]
    [InlineData("backup-error", "backup.failed", "backup-nightly")]
    [InlineData("backup-info", "backup.ok", "backup-nightly")]
    [InlineData("replication", "replication.failed", "100-0")]
    [InlineData("fencing", "node.fenced", "pve2")]
    [InlineData("updates", "updates.available", "pve1")]
    public void Notification_MapsTypeAndEntity(string fixture, string type, string entity)
    {
        WebhookEvent @event = Assert.Single(Normalize(fixture));

        Assert.Equal((type, entity), (@event.Type, @event.Entity));
    }

    [Fact]
    public void FailedBackup_PayloadHasMetadataOnly()
    {
        JsonElement payload = Assert.Single(Normalize("backup-error")).Payload;

        Assert.Equal(
            ("vzdump", "error", "pve1", "backup-nightly", DateTimeOffset.FromUnixTimeSeconds(1760000000)),
            (payload.GetProperty("type").GetString(), payload.GetProperty("severity").GetString(), payload.GetProperty("hostname").GetString(),
             payload.GetProperty("jobId").GetString(), payload.GetProperty("time").GetDateTimeOffset()));
    }

    [Fact]
    public void TitleAndMessage_AreDropped()
    {
        JsonElement payload = Assert.Single(Normalize("extra-keys")).Payload;

        Assert.Equal(["type", "severity", "hostname", "jobId", "time"], payload.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("ignore", payload.GetRawText(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SystemMail_NothingPublished()
    {
        Assert.Empty(Normalize("system-mail"));
    }

    [Fact]
    public void WithoutType_NothingPublished()
    {
        Result<IReadOnlyList<WebhookEvent>> result = _normalizer.Normalize(Request("""{"severity":"info","timestamp":1,"fields":{"hostname":"pve1"}}"""));

        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData("pve-invalid")]
    [InlineData("not json")]
    [InlineData("[1]")]
    public void Unreadable_InvalidBody(string fixtureOrBody)
    {
        string body = fixtureOrBody == "pve-invalid" ? Fixture("invalid") : fixtureOrBody;

        Assert.Equal("webhook.invalid_body", _normalizer.Normalize(Request(body)).Error.Code);
    }

    private IReadOnlyList<WebhookEvent> Normalize(string fixture)
    {
        Result<IReadOnlyList<WebhookEvent>> result = _normalizer.Normalize(Request(Fixture(fixture)));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Webhooks", "Fixtures", $"pve-{name}.json"));

    private static WebhookRequest Request(string body) => new("application/json", Encoding.UTF8.GetBytes(body), new Dictionary<string, string>());
}
