using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Media;
using Marshal.Tasks.Webhooks;

namespace Marshal.Tasks.Tests.Webhooks;

public sealed class SeerrNormalizerTests
{
    private readonly SeerrNormalizer _normalizer = new(new Links());

    [Fact]
    public void Source_IsSeerrAndTrusted() =>
        Assert.Equal(("seerr", true), (_normalizer.Source, _normalizer.Trusted));

    [Theory]
    [InlineData("available", "media.available", "movie:438631")]
    [InlineData("approved", "media.approved", "tv:95396")]
    [InlineData("auto-approved", "media.approved", "tv:95396")]
    [InlineData("declined", "media.declined", "movie:841")]
    [InlineData("failed", "media.failed", "movie:841")]
    public void Notification_MapsTypeAndEntity(string fixture, string type, string entity)
    {
        WebhookEvent @event = Assert.Single(Normalize(fixture));

        Assert.Equal((type, entity), (@event.Type, @event.Entity));
    }

    [Fact]
    public void Available_PayloadWithJellyfinLink()
    {
        JsonElement payload = Assert.Single(Normalize("available")).Payload;

        Assert.Equal(
            ("movie", 438631, 7, "abc123", "https://jellyfin.lan/web/#/details?id=abc123"),
            (payload.GetProperty("mediaType").GetString(), payload.GetProperty("tmdbId").GetInt32(), payload.GetProperty("requestId").GetInt32(),
             payload.GetProperty("jellyfinItemId").GetString(), payload.GetProperty("jellyfin").GetString()));
    }

    [Fact]
    public void NoJellyfinItem_NoLink()
    {
        JsonElement payload = Assert.Single(Normalize("approved")).Payload;

        Assert.Equal((JsonValueKind.Null, JsonValueKind.Null), (payload.GetProperty("jellyfinItemId").ValueKind, payload.GetProperty("jellyfin").ValueKind));
    }

    [Fact]
    public void FreeText_IsDropped()
    {
        JsonElement payload = Assert.Single(Normalize("extra-keys")).Payload;

        Assert.Equal(["mediaType", "tmdbId", "requestId", "jellyfinItemId", "jellyfin"], payload.EnumerateObject().Select(property => property.Name));
        Assert.DoesNotContain("Ignore", payload.GetRawText(), StringComparison.Ordinal);
        Assert.DoesNotContain("tmdb.org", payload.GetRawText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("test")]
    public void OtherNotifications_NothingPublished(string fixture)
    {
        Assert.Empty(Normalize(fixture));
    }

    [Fact]
    public void IssueNotification_NothingPublished()
    {
        Result<IReadOnlyList<WebhookEvent>> result = _normalizer.Normalize(Request("""{"notification_type":"ISSUE_CREATED","media":{"media_type":"movie","tmdbId":"841"}}"""));

        Assert.Empty(result.Value);
    }

    [Theory]
    [InlineData("seerr-no-tmdb")]
    [InlineData("not json")]
    [InlineData("[1]")]
    [InlineData("""{"notification_type":"MEDIA_AVAILABLE","media":{"media_type":"book","tmdbId":"1"}}""")]
    public void Unreadable_InvalidBody(string fixtureOrBody)
    {
        string body = fixtureOrBody == "seerr-no-tmdb" ? Fixture("no-tmdb") : fixtureOrBody;

        Assert.Equal("webhook.invalid_body", _normalizer.Normalize(Request(body)).Error.Code);
    }

    private IReadOnlyList<WebhookEvent> Normalize(string fixture)
    {
        Result<IReadOnlyList<WebhookEvent>> result = _normalizer.Normalize(Request(Fixture(fixture)));
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    private static string Fixture(string name) => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Webhooks", "Fixtures", $"seerr-{name}.json"));

    private static WebhookRequest Request(string body) => new("application/json", Encoding.UTF8.GetBytes(body), new Dictionary<string, string>());

    private sealed class Links : IJellyfinLinks
    {
        public string? ForItem(string? itemId) => string.IsNullOrEmpty(itemId) ? null : $"https://jellyfin.lan/web/#/details?id={itemId}";
    }
}
