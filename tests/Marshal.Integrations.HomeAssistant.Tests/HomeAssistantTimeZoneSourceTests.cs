using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class HomeAssistantTimeZoneSourceTests : IAsyncLifetime
{
    private readonly FixtureCache _cache = new();
    private FakeHomeAssistant _ha = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _ha = await FakeHomeAssistant.StartAsync();

    [Fact]
    public async Task Configured_WinsWithoutAskingHa()
    {
        var source = new HomeAssistantTimeZoneSource("America/New_York", _cache, Client());

        Assert.Equal("America/New_York", await source.GetAsync(Ct));
        Assert.Empty(_ha.RestRequests);
    }

    [Fact]
    public async Task NotConfigured_HaValueFromCache()
    {
        Assert.Equal("Europe/Vienna", await new HomeAssistantTimeZoneSource(null, _cache, new UnreachableClient()).GetAsync(Ct));
    }

    [Fact]
    public async Task CacheNotLoaded_AsksHaConfig()
    {
        _cache.Current = null;

        Assert.Equal("Europe/Vienna", await new HomeAssistantTimeZoneSource(null, _cache, Client()).GetAsync(Ct));
        Assert.Equal("/api/config", Assert.Single(_ha.RestRequests).Path);
    }

    [Fact]
    public async Task HaUnreachable_Utc()
    {
        _cache.Current = null;

        Assert.Equal("UTC", await new HomeAssistantTimeZoneSource(null, _cache, new UnreachableClient()).GetAsync(Ct));
    }

    public async ValueTask DisposeAsync() => await _ha.DisposeAsync();

    private HomeAssistantClient Client() =>
        new(new TestServerHttp(_ha.Server), Options.Create(new HomeAssistantOptions { Url = new Uri("http://localhost/"), Token = FakeHomeAssistant.Token }));

    private sealed class UnreachableClient : IHomeAssistantClient
    {
        public Task<Result<JsonElement>> HandleIntentAsync(HaIntentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<Result<JsonElement>>(new Error("ha.unreachable", "down", ErrorType.Unavailable));

        public Task<Result<string>> GetTimeZoneAsync(CancellationToken cancellationToken) =>
            Task.FromResult<Result<string>>(new Error("ha.unreachable", "down", ErrorType.Unavailable));

        public Task<Result<JsonElement>> GetAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<Result<JsonElement>>(new Error("ha.unreachable", "down", ErrorType.Unavailable));

        public Task<Result<JsonElement>> CallServiceAsync(string domain, string service, JsonObject data, CancellationToken cancellationToken) =>
            Task.FromResult<Result<JsonElement>>(new Error("ha.unreachable", "down", ErrorType.Unavailable));
    }
}
