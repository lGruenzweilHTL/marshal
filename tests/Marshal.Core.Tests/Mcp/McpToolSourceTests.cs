using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Marshal.Core.Mcp;
using Marshal.Core.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Marshal.Core.Tests.Mcp;

public sealed class McpToolSourceTests : IAsyncDisposable
{
    private static readonly McpReconnectPolicy _fastReconnect = new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50));

    private readonly FakeMcpServer _server = new();
    private readonly FakeMcpServerCatalog _catalog = new();
    private readonly List<McpToolSource> _sources = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_DiscoversToolsInServerNamespace()
    {
        _server.WithTool("intent__HassTurnOn", "Turns on a device.", """{"type":"object","properties":{"name":{"type":"string"}}}""");
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("ha"));

        ITool tool = Assert.Single(await source.GetToolsAsync(Ct));

        Assert.Equal(new ToolId("ha", "intent__HassTurnOn"), tool.Descriptor.Id);
        Assert.Equal("Turns on a device.", tool.Descriptor.Description);
        Assert.Equal("string", tool.Descriptor.ParametersSchema.GetProperty("properties").GetProperty("name").GetProperty("type").GetString());
        Assert.Equal("ha", source.Name);
        Assert.False(source.IsBuiltIn);
        Assert.Equal(McpServerState.Connected, source.Status.State);
        Assert.Equal(1, source.Status.ToolCount);
    }

    [Fact]
    public async Task Start_ToolPrefix_IsNamespace()
    {
        _server.WithTool("search");
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("websearch", toolPrefix: "web"));

        ITool tool = Assert.Single(await source.GetToolsAsync(Ct));

        Assert.Equal(new ToolId("web", "search"), tool.Descriptor.Id);
        Assert.Equal("websearch", source.Name);
    }

    [Fact]
    public async Task Registry_HaTool_ModelNameIsFlat()
    {
        _server.WithTool("intent__HassTurnOn").WithTool("todo.get items");
        var registry = new ToolRegistry([await StartAsync(FakeMcpServerCatalog.Server("ha"))]);

        await registry.RefreshAsync(Ct);

        Assert.NotNull(registry.FindByModelName("ha_intent__HassTurnOn"));
        Assert.NotNull(registry.FindByModelName("ha_todo_get_items"));
        Assert.Empty(registry.Errors);
    }

    [Fact]
    public async Task Registry_CollidingToolNames_RejectsServerOnly()
    {
        _server.WithTool("get.items").WithTool("get_items");
        var builtIn = new FakeToolSource("built-in", true, new FakeTool("agent.ask_advisor"));
        var registry = new ToolRegistry([builtIn, await StartAsync(FakeMcpServerCatalog.Server("todo"))]);

        await registry.RefreshAsync(Ct);

        RegisteredTool remaining = Assert.Single(registry.Tools);
        Assert.Equal("agent.ask_advisor", remaining.Id.ToString());
        ToolSourceError error = Assert.Single(registry.Errors);
        Assert.Equal("todo", error.Source);
    }

    [Fact]
    public async Task Descriptor_ExternalToolDefaultsToConfirm()
    {
        _server.WithTool("run");
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("custom"));

        ITool tool = Assert.Single(await source.GetToolsAsync(Ct));

        Assert.Equal(RiskTier.Confirm, tool.Descriptor.DefaultTier);
        Assert.False(tool.Descriptor.UntrustedOutput);
    }

    [Fact]
    public async Task Descriptor_UntrustedServer_TaintsWithServerSource()
    {
        _server.WithTool("search");
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("websearch", toolPrefix: "web", untrusted: true));

        ITool tool = Assert.Single(await source.GetToolsAsync(Ct));

        Assert.True(tool.Descriptor.UntrustedOutput);
        Assert.Equal("mcp:websearch", tool.Descriptor.TaintKind);
    }

    [Fact]
    public async Task ListChanged_ReListsTools()
    {
        _server.WithTool("first");
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("custom"));

        _server.WithTool("second");
        await _server.NotifyToolsChangedAsync(Ct);

        await EventuallyAsync(async () => (await source.GetToolsAsync(Ct)).Count == 2);
    }

    [Fact]
    public async Task Disconnect_HidesToolsAndReportsDown()
    {
        _server.WithTool("first");
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("custom"));

        _server.IsDown = true;
        await _server.DisconnectAsync();

        await EventuallyAsync(() => Task.FromResult(source.Status.State == McpServerState.Down));
        await Assert.ThrowsAsync<McpServerUnavailableException>(() => source.GetToolsAsync(Ct));
        var registry = new ToolRegistry([source]);
        await registry.RefreshAsync(Ct);
        Assert.Empty(registry.Tools);
        Assert.Contains("custom", Assert.Single(registry.Errors).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reconnect_RestoresTools()
    {
        _server.WithTool("first");
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("custom"));
        _server.IsDown = true;
        await _server.DisconnectAsync();
        await EventuallyAsync(() => Task.FromResult(source.Status.State == McpServerState.Down));

        _server.IsDown = false;

        await EventuallyAsync(() => Task.FromResult(source.Status.State == McpServerState.Connected));
        Assert.Single(await source.GetToolsAsync(Ct));
        Assert.Equal(2, _server.Connections);
    }

    [Fact]
    public async Task Start_ServerUnreachable_ReportsDownWithError()
    {
        _server.IsDown = true;

        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("custom"));

        Assert.Equal(McpServerState.Down, source.Status.State);
        Assert.Contains("Connection refused", source.Status.Error, StringComparison.Ordinal);
        await Assert.ThrowsAsync<McpServerUnavailableException>(() => source.GetToolsAsync(Ct));
    }

    [Fact]
    public async Task Start_SecretUnresolved_ReportsDownWithoutConnecting()
    {
        _catalog.ResolveError = new Error("mcp.secret_unresolved", "custom.yaml: authTokenSecret (x) does not resolve.", ErrorType.Unavailable);

        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("custom"));

        Assert.Equal(McpServerState.Down, source.Status.State);
        Assert.Equal("custom.yaml: authTokenSecret (x) does not resolve.", source.Status.Error);
        Assert.Equal(0, _server.Connections);
    }

    [Fact]
    public async Task Invoke_PassesArgumentsAndReturnsText()
    {
        _server.WithTool("intent__HassTurnOn");
        _server.Handler = (_, _) => new CallToolResult { Content = [new TextContentBlock { Text = "Turned on" }, new TextContentBlock { Text = "the light" }] };
        ITool tool = Assert.Single(await (await StartAsync(FakeMcpServerCatalog.Server("ha"))).GetToolsAsync(Ct));

        Result<ToolOutput> result = await tool.InvokeAsync(Invocation("""{"name":"Hall","domain":["light"]}"""), Ct);

        Assert.Equal("Turned on\nthe light", result.Value.Content);
        (string name, JsonElement arguments) = Assert.Single(_server.Calls);
        Assert.Equal("intent__HassTurnOn", name);
        Assert.Equal("Hall", arguments.GetProperty("name").GetString());
        Assert.Equal("light", arguments.GetProperty("domain")[0].GetString());
    }

    [Fact]
    public async Task Invoke_ToolError_IsFailedResult()
    {
        _server.WithTool("run");
        _server.Handler = (_, _) => new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "No device named Hall" }] };
        ITool tool = Assert.Single(await (await StartAsync(FakeMcpServerCatalog.Server("custom"))).GetToolsAsync(Ct));

        Result<ToolOutput> result = await tool.InvokeAsync(Invocation("{}"), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal("mcp.tool_error", result.Error.Code);
        Assert.Equal("No device named Hall", result.Error.Message);
    }

    [Fact]
    public async Task Invoke_NonTextContent_IsSummarized()
    {
        _server.WithTool("snapshot");
        _server.Handler = (_, _) => new CallToolResult
        {
            Content =
            [
                new ImageContentBlock { MimeType = "image/png", Data = "aGk="u8.ToArray() },
                new ResourceLinkBlock { Uri = "file:///report.pdf", Name = "report" },
            ],
        };
        ITool tool = Assert.Single(await (await StartAsync(FakeMcpServerCatalog.Server("custom"))).GetToolsAsync(Ct));

        Result<ToolOutput> result = await tool.InvokeAsync(Invocation("{}"), Ct);

        Assert.Equal("[image: image/png]\n[resource: report file:///report.pdf]", result.Value.Content);
    }

    [Fact]
    public async Task Invoke_ServerGone_FailsAndReconnects()
    {
        _server.WithTool("run");
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("custom"));
        ITool tool = Assert.Single(await source.GetToolsAsync(Ct));
        _server.IsDown = true;
        await _server.DisconnectAsync();

        Result<ToolOutput> result = await tool.InvokeAsync(Invocation("{}"), Ct);

        Assert.True(result.IsFailure);
        Assert.Equal("mcp.unavailable", result.Error.Code);
        _server.IsDown = false;
        await EventuallyAsync(() => Task.FromResult(source.Status.State == McpServerState.Connected && _server.Connections == 2));
        Assert.True((await (await source.GetToolsAsync(Ct))[0].InvokeAsync(Invocation("{}"), Ct)).IsSuccess);
    }

    [Theory]
    [InlineData(0, 10)]
    [InlineData(1, 20)]
    [InlineData(2, 40)]
    [InlineData(3, 50)]
    [InlineData(30, 50)]
    public void ReconnectPolicy_DoublesUpToMaximum(int attempt, int expectedMilliseconds) =>
        Assert.Equal(TimeSpan.FromMilliseconds(expectedMilliseconds), _fastReconnect.Delay(attempt));

    public async ValueTask DisposeAsync()
    {
        foreach (McpToolSource source in _sources)
            await source.DisposeAsync();

        await _server.DisposeAsync();
    }

    private async Task<McpToolSource> StartAsync(McpServerDefinition server)
    {
        var source = new McpToolSource(server, _catalog, new Transports(_server), NullLoggerFactory.Instance, _fastReconnect, TimeProvider.System);
        _sources.Add(source);
        await source.StartAsync(Ct);
        return source;
    }

    private static ToolInvocation Invocation(string arguments) =>
        new(JsonDocument.Parse(arguments).RootElement.Clone(), TestExecutionContext.Text());

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met in time.");
            await Task.Delay(10, Ct);
        }
    }

    private sealed class Transports(FakeMcpServer server) : IMcpTransportFactory
    {
        public IClientTransport Create(McpConnectionSettings settings) => server.CreateTransport();
    }
}
