using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Media.Tests;

public sealed class MediaOptionsTests
{
    [Fact]
    public void Valid_Succeeds()
    {
        Assert.Null(Failure(Valid()));
    }

    [Fact]
    public void NotConfigured_Succeeds()
    {
        Assert.Null(Failure([]));
    }

    [Fact]
    public void SeerrOnly_Succeeds()
    {
        Assert.Null(Failure(new() { ["media:seerr:url"] = "http://seerr.lan:5055/", ["media:seerr:apiKeySecret"] = "seerr_api_key" }));
    }

    [Theory]
    [InlineData("media:seerr:url", "ftp://seerr.lan/", "media.seerr ")]
    [InlineData("media:seerr:apiKeySecret", "", "media.seerr ")]
    [InlineData("media:sonarr:url", "/sonarr", "media.sonarr ")]
    [InlineData("media:radarr:apiKeySecret", "not a name", "media.radarr ")]
    [InlineData("media:jellyfinUrl", "jellyfin.lan", "media.jellyfinUrl ")]
    public void Invalid_FailsNamingTheKey(string key, string value, string expected)
    {
        Dictionary<string, string?> settings = Valid();
        settings[key] = value;

        Assert.StartsWith(expected, Failure(settings), StringComparison.Ordinal);
    }

    [Fact]
    public void SonarrWithoutSeerr_FailsNamingSeerr()
    {
        Assert.StartsWith("media.seerr.url ", Failure(new() { ["media:sonarr:url"] = "https://sonarr.lan/", ["media:sonarr:apiKeySecret"] = "sonarr_api_key" }), StringComparison.Ordinal);
    }

    [Fact]
    public void JellyfinLinks_BuiltFromBaseUrlAndItem()
    {
        var withPath = new JellyfinLinks(Options.Create(new MediaOptions { JellyfinUrl = new Uri("https://lan/jellyfin/") }));
        var plain = new JellyfinLinks(Options.Create(new MediaOptions { JellyfinUrl = new Uri("https://jellyfin.lan") }));
        var none = new JellyfinLinks(Options.Create(new MediaOptions()));

        Assert.Equal("https://lan/jellyfin/web/#/details?id=abc123", withPath.ForItem("abc123"));
        Assert.Equal("https://jellyfin.lan/web/#/details?id=abc123", plain.ForItem("abc123"));
        Assert.Null(plain.ForItem(null));
        Assert.Null(none.ForItem("abc123"));
    }

    private static Dictionary<string, string?> Valid() => new()
    {
        ["media:seerr:url"] = "https://seerr.lan/",
        ["media:seerr:apiKeySecret"] = "seerr_api_key",
        ["media:sonarr:url"] = "https://sonarr.lan/",
        ["media:sonarr:apiKeySecret"] = "sonarr_api_key",
        ["media:radarr:url"] = "https://lan/radarr/",
        ["media:radarr:apiKeySecret"] = "radarr_api_key",
        ["media:jellyfinUrl"] = "https://jellyfin.lan/",
    };

    private static string? Failure(Dictionary<string, string?> settings)
    {
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        using ServiceProvider services = new ServiceCollection().AddMarshalMedia(configuration).BuildServiceProvider();
        try
        {
            _ = services.GetRequiredService<IOptions<MediaOptions>>().Value;
            return null;
        }
        catch (OptionsValidationException failure)
        {
            return failure.Failures.First();
        }
    }
}
