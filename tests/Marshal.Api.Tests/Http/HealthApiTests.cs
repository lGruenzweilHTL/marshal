using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Core.Secrets;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class HealthApiTests
{
    [Fact]
    public async Task Health_Anonymous_ReportsChecks()
    {
        using var factory = new MarshalApiFactory();

        HttpResponseMessage response = await factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("healthy", body.GetProperty("status").GetString());
        JsonElement checks = body.GetProperty("checks");
        Assert.Equal("healthy", checks.GetProperty("database").GetProperty("status").GetString());
        Assert.Equal("healthy", checks.GetProperty("config").GetProperty("status").GetString());
        Assert.Equal("healthy", checks.GetProperty("secrets").GetProperty("status").GetString());
    }

    [Fact]
    public async Task Health_MissingOptionalSecret_WarningNamesReferenceNotValue()
    {
        using var factory = new MarshalApiFactory();
        HttpClient client = factory.CreateClient();
        factory.Services.GetRequiredService<SecretHealth>().Set(["printers[0].apiKeySecret (moonraker_voron)"]);

        string body = await client.GetStringAsync("/health", Ct);

        Assert.Contains("\"degraded\"", body, StringComparison.Ordinal);
        Assert.Contains("printers[0].apiKeySecret (moonraker_voron)", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_FailedConfigReload_Degraded()
    {
        using var factory = new MarshalApiFactory();
        HttpClient client = factory.CreateClient();
        await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "satellites.yaml"), "satellites: [broken\n", Ct);
        factory.Services.GetRequiredService<Core.Configuration.YamlConfigFile<Core.Satellites.SatellitesFile>>().Reload();

        string body = await client.GetStringAsync("/health", Ct);

        Assert.Contains("satellites.yaml", body, StringComparison.Ordinal);
        Assert.Contains("\"degraded\"", body, StringComparison.Ordinal);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
