using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Mcp;
using Marshal.Api.Health;
using Marshal.Core.Mcp;
using Marshal.Core.Secrets;
using Marshal.Data;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

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
        Assert.Equal("healthy", checks.GetProperty("mcp").GetProperty("status").GetString());
        Assert.Equal("healthy", checks.GetProperty("memory").GetProperty("status").GetString());
    }

    [Fact]
    public async Task MemoryCheck_WithoutVectorExtension_Unhealthy()
    {
        string path = Path.Combine(Path.GetTempPath(), $"marshal-plain-{Guid.NewGuid():N}.db");
        DbContextOptions<MarshalDbContext> options = new DbContextOptionsBuilder<MarshalDbContext>().UseSqlite($"Data Source={path}").Options;
        var check = new MemoryHealthCheck(new PlainFactory(options));

        HealthCheckResult result = await check.CheckHealthAsync(new HealthCheckContext(), Ct);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
        Assert.Contains("sqlite-vec", result.Description, StringComparison.Ordinal);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        File.Delete(path);
    }

    [Fact]
    public async Task Health_McpServerDownAndInvalidFile_WarningsNameThem()
    {
        using var factory = new MarshalApiFactory();
        Directory.CreateDirectory(Path.Combine(factory.ConfigDirectory, "mcp"));
        await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "mcp", "search.yaml"), "id: search\ntransport: http\nurl: http://127.0.0.1:1/mcp\n", Ct);
        await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "mcp", "bad.yaml"), "id: home_assistant\ntransport: http\nurl: http://ha/api/mcp\n", Ct);

        HttpResponseMessage response = await factory.CreateClient().GetAsync("/health", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement mcp = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("checks").GetProperty("mcp");
        Assert.Equal("degraded", mcp.GetProperty("status").GetString());
        string[] warnings = [.. mcp.GetProperty("warnings").EnumerateArray().Select(warning => warning.GetString()!)];
        Assert.Contains(warnings, warning => warning.StartsWith("MCP server search is down", StringComparison.Ordinal));
        Assert.Contains(warnings, warning => warning.StartsWith("mcp/bad.yaml: ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Health_UntrustedToolsGlobMatchingNothing_WarningNamesTheGlob()
    {
        await using var server = new FakeMcpServer();
        server.WithTool("todo__get_items");
        using var factory = new MarshalApiFactory();
        factory.TestServices = services => services.AddSingleton<IMcpTransportFactory>(new FakeTransports(server));
        Directory.CreateDirectory(Path.Combine(factory.ConfigDirectory, "mcp"));
        await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "mcp", "ha.yaml"), "id: ha\ntransport: http\nurl: http://ha/api/mcp\nuntrustedTools: [todo__get_itemz]\n", Ct);

        HttpResponseMessage response = await factory.CreateClient().GetAsync("/health", Ct);

        JsonElement mcp = (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("checks").GetProperty("mcp");
        Assert.Equal("degraded", mcp.GetProperty("status").GetString());
        Assert.Contains("1 of 1 servers connected", mcp.GetProperty("description").GetString(), StringComparison.Ordinal);
        string warning = Assert.Single(mcp.GetProperty("warnings").EnumerateArray()).GetString()!;
        Assert.Contains("todo__get_itemz", warning, StringComparison.Ordinal);
    }

    private sealed class FakeTransports(FakeMcpServer server) : IMcpTransportFactory
    {
        public ModelContextProtocol.Client.IClientTransport Create(McpConnectionSettings settings) => server.CreateTransport();
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

    [Fact]
    public async Task Health_RejectedSkill_ConfigWarning()
    {
        using var factory = new MarshalApiFactory();
        Directory.CreateDirectory(Path.Combine(factory.ConfigDirectory, "skills", "broken"));
        await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "skills", "broken", "SKILL.md"), "---\nname: broken\n---\nBody\n", Ct);

        string body = await factory.CreateClient().GetStringAsync("/health", Ct);

        Assert.Contains("skills/broken/SKILL.md", body, StringComparison.Ordinal);
    }

    private sealed class PlainFactory(DbContextOptions<MarshalDbContext> options) : IDbContextFactory<MarshalDbContext>
    {
        public MarshalDbContext CreateDbContext() => new(options, new FakeExecutionContextAccessor());
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
