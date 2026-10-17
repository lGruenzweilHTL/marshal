using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Auth;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Http;

public sealed class SystemApiTests : IAsyncLifetime
{
    private readonly MarshalApiFactory _factory = new(AuthFactory.Local().ToSettings(0));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task System_SignedIn_ShowsAppVersionAndApiVersions()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        JsonElement system = await browser.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct);

        Assert.False(string.IsNullOrWhiteSpace(system.GetProperty("version").GetString()));
        Assert.Contains("1.0", system.GetProperty("supportedApiVersions").EnumerateArray().Select(version => version.GetString()));
        Assert.Equal(JsonValueKind.Array, system.GetProperty("deprecatedApiVersions").ValueKind);
    }

    [Fact]
    public async Task System_ListsUptimeKumaWithItsLastReceivedTime()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        static JsonElement Kuma(JsonElement system) => system.GetProperty("eventSources").EnumerateArray().Single(source => source.GetProperty("source").GetString() == "uptime-kuma");

        JsonElement before = Kuma(await browser.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct));
        HttpClient hook = await _factory.ClientWithTokenAsync(ApiScopes.HooksUptimeKuma);
        DateTimeOffset sent = DateTimeOffset.UtcNow;
        var test = new StringContent("""{"heartbeat":null,"monitor":null,"msg":"Testing"}""", System.Text.Encoding.UTF8, "application/json");
        Assert.Equal(HttpStatusCode.Accepted, (await hook.PostAsync("/api/v1/hooks/uptime-kuma", test, Ct)).StatusCode);
        JsonElement after = Kuma(await browser.GetFromJsonAsync<JsonElement>("/api/v1/system", Ct));

        Assert.Equal((false, JsonValueKind.Null), (before.GetProperty("connected").GetBoolean(), before.GetProperty("lastReceivedAt").ValueKind));
        Assert.True(after.GetProperty("connected").GetBoolean());
        Assert.True(after.GetProperty("webhook").GetBoolean());
        Assert.InRange(after.GetProperty("lastReceivedAt").GetDateTimeOffset(), sent.AddSeconds(-1), DateTimeOffset.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task System_Token_IsAllowed()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.TasksRead);

        HttpResponseMessage response = await client.GetAsync("/api/v1/system", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task System_Anonymous_Returns401()
    {
        HttpResponseMessage response = await _factory.CreateClient().GetAsync("/api/v1/system", Ct);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Health_NoWarnings_EmptyList()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        JsonElement health = await browser.GetFromJsonAsync<JsonElement>("/api/v1/system/health", Ct);

        Assert.Empty(health.GetProperty("warnings").EnumerateArray());
    }

    [Fact]
    public async Task Health_McpServerDown_CodedWarningWithoutTransportError()
    {
        Directory.CreateDirectory(Path.Combine(_factory.ConfigDirectory, "mcp"));
        await File.WriteAllTextAsync(Path.Combine(_factory.ConfigDirectory, "mcp", "paperless.yaml"), "id: paperless\ntransport: http\nurl: http://127.0.0.1:1/mcp\n", Ct);
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");

        string body = await browser.GetStringAsync("/api/v1/system/health", Ct);

        JsonElement warning = Assert.Single(JsonDocument.Parse(body).RootElement.GetProperty("warnings").EnumerateArray());
        Assert.Equal("mcp:paperless", warning.GetProperty("component").GetString());
        Assert.Equal("mcp.unreachable", warning.GetProperty("code").GetString());
        Assert.Equal("warning", warning.GetProperty("severity").GetString());
        Assert.Equal("The MCP server paperless is unreachable.", warning.GetProperty("message").GetProperty("en").GetString());
        Assert.Equal("Der MCP-Server paperless ist nicht erreichbar.", warning.GetProperty("message").GetProperty("de").GetString());
        Assert.Equal("/system/config", warning.GetProperty("link").GetString());
        Assert.DoesNotContain("127.0.0.1", body, StringComparison.Ordinal);
        Assert.DoesNotContain("refused", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_MissingSecretAndFailedReload_NamedWithoutValues()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        _factory.Services.GetRequiredService<Core.Secrets.SecretHealth>().Set(["printers[0].apiKeySecret (moonraker_voron)"]);
        await File.WriteAllTextAsync(Path.Combine(_factory.ConfigDirectory, "satellites.yaml"), "satellites: [broken\n", Ct);
        _factory.Services.GetRequiredService<Core.Configuration.YamlConfigFile<Core.Satellites.SatellitesFile>>().Reload();

        JsonElement health = await browser.GetFromJsonAsync<JsonElement>("/api/v1/system/health", Ct);

        string[] codes = [.. health.GetProperty("warnings").EnumerateArray().Select(warning => $"{warning.GetProperty("component").GetString()} {warning.GetProperty("code").GetString()}")];
        Assert.Equal(["config:satellites.yaml config.reload_failed", "secrets:printers[0].apiKeySecret (moonraker_voron) secrets.missing"], codes);
    }

    [Fact]
    public async Task Health_AdminToken_IsAllowed()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.Admin);

        HttpResponseMessage response = await client.GetAsync("/api/v1/system/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Health_TokenWithoutAdmin_Returns403()
    {
        HttpClient client = await _factory.ClientWithTokenAsync(ApiScopes.TasksRead);

        HttpResponseMessage response = await client.GetAsync("/api/v1/system/health", Ct);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Health_IsInTheOpenApiDocument()
    {
        JsonElement document = await _factory.CreateClient().GetFromJsonAsync<JsonElement>("/openapi/v1.json", Ct);

        Assert.True(document.GetProperty("paths").TryGetProperty("/api/v1/system/health", out _));
    }
}
