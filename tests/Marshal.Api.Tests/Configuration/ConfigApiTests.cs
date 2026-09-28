using System.Net;
using System.Text.Json;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Core.Configuration;
using Marshal.Core.Guard;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Configuration;

public sealed class ConfigApiTests : IAsyncLifetime
{
    private const string ApiKey = "sk-ant-api03-very-secret-value";
    private const string McpToken = "mcp-token-very-secret";

    private MarshalApiFactory _factory = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["models:claude:tier"] = "advisor";
        settings["models:claude:provider"] = "anthropic";
        settings["models:claude:model"] = "claude-opus-4-1";
        settings["models:claude:apiKeySecret"] = "anthropic_key";
        _factory = new MarshalApiFactory(settings);
        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "anthropic_key"), ApiKey, Ct);
        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "tools_token"), McpToken, Ct);

        string config = _factory.ConfigDirectory;
        await File.WriteAllTextAsync(Path.Combine(config, "risk.yaml"), """
            risk:
              - match: { tool: "ha.*", domain: lock }
                tier: confirm
            """, Ct);
        await File.WriteAllTextAsync(Path.Combine(config, "satellites.yaml"), """
            satellites:
              assist_satellite.kids_room:
                verbosity: short
                denyTools: ["mail.*"]
            """, Ct);
        Directory.CreateDirectory(Path.Combine(config, "skills", "weather"));
        await File.WriteAllTextAsync(Path.Combine(config, "skills", "weather", "SKILL.md"), "---\nname: weather\ndescription: Answers weather questions.\n---\nUse the forecast.\n", Ct);
        Directory.CreateDirectory(Path.Combine(config, "skills", "broken"));
        await File.WriteAllTextAsync(Path.Combine(config, "skills", "broken", "SKILL.md"), "no frontmatter", Ct);
        Directory.CreateDirectory(Path.Combine(config, "templates"));
        await File.WriteAllTextAsync(Path.Combine(config, "templates", "rss-scorer.md"), "---\nname: rss-scorer\ndescription: Scores feed items.\nmodelTier: fast\ntools: [\"rss.*\"]\n---\nScore items.\n", Ct);
        Directory.CreateDirectory(Path.Combine(config, "mcp"));
        await File.WriteAllTextAsync(Path.Combine(config, "mcp", "tools.yaml"), "id: tools\ntransport: http\nurl: http://127.0.0.1:1/mcp\nauthTokenSecret: tools_token\n", Ct);
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Summary_ListsLoadedConfigWithoutSecrets()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "admin");

        string body = await browser.GetStringAsync("/api/v1/config", Ct);

        Assert.DoesNotContain(ApiKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain(McpToken, body, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(body);
        JsonElement root = document.RootElement;
        JsonElement skill = Assert.Single(root.GetProperty("skills").EnumerateArray());
        Assert.Equal(("weather", "Answers weather questions."), (skill.GetProperty("name").GetString(), skill.GetProperty("description").GetString()));
        JsonElement scorer = root.GetProperty("templates").EnumerateArray().Single(template => template.GetProperty("name").GetString() == "rss-scorer");
        Assert.Equal("fast", scorer.GetProperty("modelTier").GetString());
        Assert.Equal(["rss.*"], scorer.GetProperty("tools").EnumerateArray().Select(tool => tool.GetString()));
        Assert.Contains(root.GetProperty("templates").EnumerateArray(), template => template.GetProperty("name").GetString() == "default" && template.GetProperty("builtIn").GetBoolean());
        JsonElement server = Assert.Single(root.GetProperty("mcpServers").EnumerateArray());
        Assert.Equal(("tools", "http://127.0.0.1:1/mcp", "tools_token"), (server.GetProperty("id").GetString(), server.GetProperty("target").GetString(), server.GetProperty("authTokenSecret").GetString()));
        Assert.NotEqual("connected", server.GetProperty("state").GetString());
        Assert.Equal(0, server.GetProperty("toolCount").GetInt32());
        JsonElement rule = Assert.Single(root.GetProperty("riskRules").EnumerateArray());
        Assert.Equal(("ha.*", "confirm"), (rule.GetProperty("tool").GetString(), rule.GetProperty("tier").GetString()));
        Assert.Equal(["lock"], rule.GetProperty("match").GetProperty("domain").EnumerateArray().Select(value => value.GetString()));
        JsonElement claude = root.GetProperty("models").EnumerateArray().Single(model => model.GetProperty("key").GetString() == "claude");
        Assert.Equal(("anthropic", "claude-opus-4-1", "anthropic_key"), (claude.GetProperty("provider").GetString(), claude.GetProperty("model").GetString(), claude.GetProperty("apiKeySecret").GetString()));
        JsonElement satellite = Assert.Single(root.GetProperty("satellites").EnumerateArray());
        Assert.Equal(("assist_satellite.kids_room", "short"), (satellite.GetProperty("id").GetString(), satellite.GetProperty("verbosity").GetString()));
        JsonElement broken = Assert.Single(root.GetProperty("errors").EnumerateArray());
        Assert.Equal("skills/broken/SKILL.md", broken.GetProperty("file").GetString());
        Assert.False(broken.GetProperty("previousVersionActive").GetBoolean());
    }

    [Fact]
    public async Task Summary_FailedRiskReload_ShowsErrorAndPreviousRulesActive()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "admin");
        await File.WriteAllTextAsync(Path.Combine(_factory.ConfigDirectory, "risk.yaml"), "risk:\n  - match: { tool: \"ha.*\" }\n    tier: maybe\n", Ct);
        _factory.Services.GetRequiredService<YamlConfigFile<RiskRulesFile>>().Reload();

        using var document = JsonDocument.Parse(await browser.GetStringAsync("/api/v1/config", Ct));

        JsonElement error = document.RootElement.GetProperty("errors").EnumerateArray().Single(entry => entry.GetProperty("file").GetString() == "risk.yaml");
        Assert.Contains("tier", error.GetProperty("message").GetString(), StringComparison.Ordinal);
        Assert.True(error.GetProperty("previousVersionActive").GetBoolean());
        Assert.Equal("confirm", Assert.Single(document.RootElement.GetProperty("riskRules").EnumerateArray()).GetProperty("tier").GetString());
    }

    [Fact]
    public async Task Summary_WithoutAdmin_Returns403()
    {
        (HttpClient browser, _) = await Browser.SignInAsync(_factory, "user");
        HttpClient machine = await _factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);

        Assert.Equal(HttpStatusCode.Forbidden, (await browser.GetAsync("/api/v1/config", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await machine.GetAsync("/api/v1/config", Ct)).StatusCode);
    }
}
