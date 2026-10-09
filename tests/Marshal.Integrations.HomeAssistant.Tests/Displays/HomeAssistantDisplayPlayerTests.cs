using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Displays;
using Marshal.Integrations.HomeAssistant.Displays;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Tests.Displays;

public sealed class HomeAssistantDisplayPlayerTests : IAsyncLifetime
{
    private static readonly DisplaySurface _livingTv = new("living_tv", "media_player.living_room_tv");
    private FakeHomeAssistant _ha = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _ha = await FakeHomeAssistant.StartAsync();

    public async ValueTask DisposeAsync() => await _ha.DisposeAsync();

    [Theory]
    [InlineData(DisplayMediaType.Image, "image")]
    [InlineData(DisplayMediaType.Video, "video")]
    public async Task Play_CallsPlayMediaOnTheEntity(DisplayMediaType type, string contentType)
    {
        Result result = await Player().PlayAsync(_livingTv, "http://192.168.1.10:8080/a/1/sig.jpg", type, Ct);

        Assert.True(result.IsSuccess);
        (string path, JsonNode? body) = Assert.Single(_ha.RestRequests);
        Assert.Equal("/api/services/media_player/play_media", path);
        Assert.Equal(
            ("media_player.living_room_tv", "http://192.168.1.10:8080/a/1/sig.jpg", contentType),
            ((string?)body!["entity_id"], (string?)body["media_content_id"], (string?)body["media_content_type"]));
    }

    [Fact]
    public async Task Play_HaRejects_DisplayFailedNamingTheDisplay()
    {
        _ha.FailingServices.Add("media_player/play_media");

        Result result = await Player().PlayAsync(_livingTv, "http://x/1.png", DisplayMediaType.Image, Ct);

        Assert.Equal("display.failed", result.Error.Code);
        Assert.Contains("living_tv", result.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Play_HaUnreachable_Unreachable()
    {
        var client = new HomeAssistantClient(new UnreachableHttp(), Options.Create(new HomeAssistantOptions { Url = new Uri("http://127.0.0.1:1/"), Token = FakeHomeAssistant.Token }));

        Result result = await new HomeAssistantDisplayPlayer(client, Options.Create(new HomeAssistantOptions())).PlayAsync(_livingTv, "http://x/1.png", DisplayMediaType.Image, Ct);

        Assert.Equal("ha.unreachable", result.Error.Code);
    }

    [Fact]
    public void IsAvailable_OnlyWithHaUrl()
    {
        Assert.True(Player().IsAvailable);
        Assert.False(new HomeAssistantDisplayPlayer(new HomeAssistantClient(new UnreachableHttp(), Options.Create(new HomeAssistantOptions())), Options.Create(new HomeAssistantOptions())).IsAvailable);
    }

    private HomeAssistantDisplayPlayer Player()
    {
        IOptions<HomeAssistantOptions> options = Options.Create(new HomeAssistantOptions { Url = new Uri("http://localhost/"), Token = FakeHomeAssistant.Token });
        return new(new HomeAssistantClient(new TestServerHttp(_ha.Server), options), options);
    }

    private sealed class UnreachableHttp : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(new Failing());

        private sealed class Failing : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
                throw new HttpRequestException("Connection refused");
        }
    }
}
