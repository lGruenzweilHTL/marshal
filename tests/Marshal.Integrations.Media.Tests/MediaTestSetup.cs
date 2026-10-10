using Marshal.Testing;

namespace Marshal.Integrations.Media.Tests;

internal static class MediaTestSetup
{
    public static MediaOptions Options(bool arr = true, string? jellyfin = "https://jellyfin.lan/") => new()
    {
        Seerr = new MediaService { Url = new Uri("https://media.lan/seerr/"), ApiKeySecret = "seerr_api_key" },
        Sonarr = arr ? new MediaService { Url = new Uri("https://media.lan/sonarr/"), ApiKeySecret = "sonarr_api_key" } : null,
        Radarr = arr ? new MediaService { Url = new Uri("https://media.lan/radarr"), ApiKeySecret = "radarr_api_key" } : null,
        JellyfinUrl = jellyfin is null ? null : new Uri(jellyfin),
    };

    public static FakeSecretStore Secrets()
    {
        var secrets = new FakeSecretStore();
        secrets.Values["seerr_api_key"] = FakeMediaServer.SeerrKey + "\n";
        secrets.Values["sonarr_api_key"] = FakeMediaServer.SonarrKey;
        secrets.Values["radarr_api_key"] = FakeMediaServer.RadarrKey;
        return secrets;
    }

    public static SeerrRestClient Seerr(FakeMediaServer server, MediaOptions? options = null, FakeSecretStore? secrets = null) =>
        new(new Http(server), Microsoft.Extensions.Options.Options.Create(options ?? Options()), secrets ?? Secrets());

    public static ArrRestClient Arr(FakeMediaServer server, MediaOptions? options = null, FakeSecretStore? secrets = null) =>
        new(new Http(server), Microsoft.Extensions.Options.Options.Create(options ?? Options()), secrets ?? Secrets());

    private sealed class Http(FakeMediaServer server) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(server.Server.CreateHandler());
    }
}
