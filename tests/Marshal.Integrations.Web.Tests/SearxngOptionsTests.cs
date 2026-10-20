using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Web.Tests;

public sealed class SearxngOptionsTests
{
    [Fact]
    public void Defaults_EnginesAndMaxResults()
    {
        SearxngOptions options = Options(("web:searxng:url", "http://searxng.lan:8080"));

        Assert.Equal(["google", "bing", "duckduckgo", "wikipedia"], options.EffectiveEngines);
        Assert.Equal(8, options.MaxResults);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public void Engines_ReplaceTheDefaults()
    {
        SearxngOptions options = Options(("web:searxng:url", "http://searxng.lan"), ("web:searxng:engines:0", "wikipedia"));

        Assert.Equal(["wikipedia"], options.EffectiveEngines);
    }

    [Fact]
    public void NoSection_NotConfigured() => Assert.False(Options().IsConfigured);

    [Theory]
    [InlineData("web:searxng:url", "searxng.lan", "web.searxng.url")]
    [InlineData("web:searxng:url", "ftp://searxng.lan", "web.searxng.url")]
    [InlineData("web:searxng:authHeaderSecret", "Bad-Name", "web.searxng.authHeaderSecret")]
    [InlineData("web:searxng:maxResults", "0", "web.searxng.maxResults")]
    [InlineData("web:searxng:maxResults", "21", "web.searxng.maxResults")]
    [InlineData("web:searxng:engines:0", " ", "web.searxng.engines")]
    public void Invalid_FailsNamingTheKey(string key, string value, string named)
    {
        OptionsValidationException error = Assert.Throws<OptionsValidationException>(() => Options(("web:searxng:url", "http://searxng.lan"), (key, value)));

        Assert.Contains(named, error.Message, StringComparison.Ordinal);
    }

    private static SearxngOptions Options(params (string Key, string Value)[] values)
    {
        // Later values replace earlier ones, so a test can override the URL.
        var settings = new Dictionary<string, string?>();
        foreach ((string key, string value) in values)
            settings[key] = value;
        IConfiguration configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddMarshalWeb(configuration);
        return services.BuildServiceProvider().GetRequiredService<IOptions<SearxngOptions>>().Value;
    }
}
