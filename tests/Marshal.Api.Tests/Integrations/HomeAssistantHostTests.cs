using System.Text.Json;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tools;
using Marshal.Api.Tests.Http;
using Marshal.Integrations.HomeAssistant;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Integrations;

public sealed class HomeAssistantHostTests
{
    private static readonly Dictionary<string, string?> _ha = new()
    {
        ["ha:url"] = "http://127.0.0.1:1",
        ["ha:tokenSecret"] = "ha_token",
    };

    [Fact]
    public void Startup_TokenSecretMissing_FailsNamingReference()
    {
        using var factory = new MarshalApiFactory(new Dictionary<string, string?>(_ha));

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ha.tokenSecret (ha_token)", StartupErrors.Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_UrlWithoutTokenSecret_Fails()
    {
        using var factory = new MarshalApiFactory(new Dictionary<string, string?> { ["ha:url"] = "http://127.0.0.1:1" });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains("ha.tokenSecret", StartupErrors.Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Configured_WiresResolverContextTimersAndTimeZone()
    {
        using var factory = new MarshalApiFactory(new Dictionary<string, string?>(_ha));
        await File.WriteAllTextAsync(Path.Combine(factory.SecretsDirectory, "ha_token"), "token\n", Ct);
        HttpClient client = factory.CreateClient();
        IServiceProvider services = factory.Services;

        Assert.Contains(services.GetServices<IRiskTargetResolver>(), resolver => resolver is HaTargetResolver);
        Assert.IsType<HaAreaDirectory>(services.GetRequiredService<IAreaDirectory>());
        Assert.IsType<HomeAssistantTimeZoneSource>(services.GetRequiredService<IInstanceTimeZone>());
        Assert.Equal("token", services.GetRequiredService<Microsoft.Extensions.Options.IOptions<HomeAssistantOptions>>().Value.Token);
        ToolSnapshot tools = await services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);
        Assert.NotNull(tools.FindById(new ToolId("timers", "start")));

        using var health = JsonDocument.Parse(await client.GetStringAsync("/health", Ct));
        Assert.Equal("degraded", health.RootElement.GetProperty("checks").GetProperty("homeassistant").GetProperty("status").GetString());
    }

    [Fact]
    public async Task NotConfigured_InertWithoutTimersOrConnection()
    {
        using var factory = new MarshalApiFactory();
        HttpClient client = factory.CreateClient();

        ToolSnapshot tools = await factory.Services.GetRequiredService<IToolRegistry>().RefreshAsync(Ct);
        Assert.Null(tools.FindById(new ToolId("timers", "start")));
        Assert.Null(factory.Services.GetRequiredService<IHomeAssistantCache>().Current);
        using var health = JsonDocument.Parse(await client.GetStringAsync("/health", Ct));
        Assert.Equal("healthy", health.RootElement.GetProperty("checks").GetProperty("homeassistant").GetProperty("status").GetString());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
