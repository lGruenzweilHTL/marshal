using Marshal.Abstractions.Configuration;
using Marshal.Api.Hosting;
using Marshal.Api.Tests.Http;

namespace Marshal.Api.Tests.Hosting;

public sealed class ConfigurationStageTests
{
    [Fact]
    public void Startup_EmbeddingMissing_ReportsEveryEmbeddingKey()
    {
        using var factory = new MarshalApiFactory(new() { ["embedding:provider"] = "", ["embedding:model"] = "" });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        StartupFailedException failure = StartupErrors.Reported(error);
        Assert.Contains(new StartupError("marshal.yaml", "embedding.provider", "must be ollama or openai-compatible."), failure.Errors);
        Assert.Contains(new StartupError("marshal.yaml", "embedding.model", "is required."), failure.Errors);
    }

    [Fact]
    public void Startup_AttributeValidatedOptionInvalid_ReportsTheAttributeMessage()
    {
        using var factory = new MarshalApiFactory(new() { ["agent:maxSteps"] = "0" });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(new StartupError("marshal.yaml", "agent.maxSteps", "must be between 1 and 50."), StartupErrors.Reported(error).Errors);
    }

    [Fact]
    public async Task Startup_InvalidRiskFileAndInvalidOption_ReportsBoth()
    {
        using var factory = new MarshalApiFactory(new() { ["embedding:model"] = "" });
        await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "risk.yaml"), "risk: [\n", TestContext.Current.CancellationToken);

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        IReadOnlyList<StartupError> errors = StartupErrors.Reported(error).Errors;
        Assert.Contains(errors, reported => reported is { Source: "risk.yaml", Key: null });
        Assert.Contains(new StartupError("marshal.yaml", "embedding.model", "is required."), errors);
    }

    [Fact]
    public void Startup_ConfigurationStageFails_NeverOpensTheDatabase()
    {
        using var factory = new MarshalApiFactory(new() { ["embedding:model"] = "", ["ha:url"] = "http://127.0.0.1:1", ["ha:tokenSecret"] = "ha_token" });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.DoesNotContain(StartupErrors.Reported(error).Errors, reported => reported.Key == "ha.tokenSecret");
        Assert.False(File.Exists(Path.Combine(factory.Root, "data", "marshal.db")));
    }

    [Fact]
    public void Startup_DisplayWithoutBaseUrl_ReportsMediaBaseUrl()
    {
        using var factory = new MarshalApiFactory(new() { ["displays:surfaces:living_tv:entity"] = "media_player.living_room_tv" });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(StartupErrors.Reported(error).Errors, reported => reported is { Source: "marshal.yaml", Key: "displays.mediaBaseUrl" });
    }

    [Fact]
    public void Startup_DisplayWithPublicUrlFallback_Starts()
    {
        using var factory = new MarshalApiFactory(new()
        {
            ["displays:surfaces:living_tv:entity"] = "media_player.living_room_tv",
            ["notifications:publicUrl"] = "https://marshal.example.com/",
        });

        Assert.NotNull(factory.CreateClient());
    }

    [Fact]
    public void Startup_DisplayWithWrongEntity_ReportsTheKey()
    {
        using var factory = new MarshalApiFactory(new()
        {
            ["displays:mediaBaseUrl"] = "http://192.168.1.10:8080/",
            ["displays:surfaces:living_tv:entity"] = "light.living_room",
        });

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        Assert.Contains(StartupErrors.Reported(error).Errors, reported => reported is { Source: "marshal.yaml", Key: "displays.surfaces.living_tv.entity" });
    }

    [Fact]
    public async Task Startup_SatelliteWithUnknownDisplay_ReportsSatellitesFile()
    {
        using var factory = new MarshalApiFactory(new());
        await File.WriteAllTextAsync(
            Path.Combine(factory.ConfigDirectory, "satellites.yaml"),
            "satellites:\n  assist_satellite.kitchen:\n    display: kitchen_tv\n",
            TestContext.Current.CancellationToken);

        Exception error = Assert.ThrowsAny<Exception>(() => factory.CreateClient());

        StartupError reported = Assert.Single(StartupErrors.Reported(error).Errors, entry => entry.Source == "satellites.yaml");
        Assert.Contains("satellites.assist_satellite.kitchen.display", reported.Message, StringComparison.Ordinal);
    }
}
