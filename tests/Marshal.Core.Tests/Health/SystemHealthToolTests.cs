using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Health;
using Marshal.Abstractions.Tools;
using Marshal.Core.Health;
using Marshal.Core.Tools;
using Marshal.Testing;

namespace Marshal.Core.Tests.Health;

public sealed class SystemHealthToolTests
{
    private static readonly DateTimeOffset _since = new(2026, 10, 16, 7, 30, 0, TimeSpan.Zero);

    private readonly Health _health = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Descriptor_SafeTrustedSystemNamespace()
    {
        var tool = new SystemHealthTool(_health);

        Assert.Equal(new ToolId("system", "health"), tool.Descriptor.Id);
        Assert.Equal(RiskTier.Safe, tool.Descriptor.DefaultTier);
        Assert.False(tool.Descriptor.UntrustedOutput);
        Assert.Contains("system", ReservedNamespaces.All);
    }

    [Fact]
    public async Task NoWarnings_Hidden()
    {
        var tool = new SystemHealthTool(_health);

        Assert.False(await tool.IsAvailableAsync(TestExecutionContext.Text(), Ct));
    }

    [Fact]
    public async Task NoSystemHealth_Hidden()
    {
        var tool = new SystemHealthTool();

        Assert.False(await tool.IsAvailableAsync(TestExecutionContext.Text(), Ct));
    }

    [Fact]
    public async Task Warning_VisibleAndReturnedInRunLanguage()
    {
        _health.Warnings = [Warning()];
        var tool = new SystemHealthTool(_health);

        Assert.True(await tool.IsAvailableAsync(TestExecutionContext.Text(), Ct));
        Result<ToolOutput> german = await tool.InvokeAsync(new ToolInvocation(Empty(), TestExecutionContext.Text() with { Language = "de" }), Ct);
        Result<ToolOutput> english = await tool.InvokeAsync(new ToolInvocation(Empty(), TestExecutionContext.Text()), Ct);

        JsonElement warning = Assert.Single(JsonDocument.Parse(german.Value.Content).RootElement.GetProperty("warnings").EnumerateArray());
        Assert.Equal("mcp:paperless", warning.GetProperty("component").GetString());
        Assert.Equal("mcp.unreachable", warning.GetProperty("code").GetString());
        Assert.Equal("warning", warning.GetProperty("severity").GetString());
        Assert.Equal("Der MCP-Server paperless ist nicht erreichbar.", warning.GetProperty("message").GetString());
        Assert.Equal(_since, warning.GetProperty("since").GetDateTimeOffset());
        Assert.Contains("The MCP server paperless is unreachable.", english.Value.Content, StringComparison.Ordinal);
    }

    private static JsonElement Empty() => JsonDocument.Parse("{}").RootElement.Clone();

    private static HealthWarning Warning() => new("mcp:paperless", "mcp.unreachable", HealthSeverity.Warning,
        new HealthText("Der MCP-Server paperless ist nicht erreichbar.", "The MCP server paperless is unreachable."), "/system/config", _since);

    private sealed class Health : ISystemHealth
    {
        public IReadOnlyList<HealthWarning> Warnings { get; set; } = [];

        public IReadOnlyList<HealthWarning> Last => Warnings;

        public Task<IReadOnlyList<HealthWarning>> GetWarningsAsync(CancellationToken cancellationToken) => Task.FromResult(Warnings);
    }
}
