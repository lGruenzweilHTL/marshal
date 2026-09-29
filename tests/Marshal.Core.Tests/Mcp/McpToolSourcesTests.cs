using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Marshal.Core.Mcp;
using Marshal.Core.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Marshal.Core.Tests.Mcp;

public sealed class McpToolSourcesTests : IAsyncDisposable
{
    private readonly FakeMcpServerCatalog _catalog = new();
    private readonly Dictionary<string, FakeMcpServer> _servers = new(StringComparer.Ordinal);
    private McpToolSources? _sources;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Start_OneConnectedSourcePerServer()
    {
        AddServer("ha", "intent__HassTurnOn");
        AddServer("search", "query");

        McpToolSources sources = await StartAsync();

        Assert.Equal(["ha", "search"], ((IToolSourceProvider)sources).Sources.Select(source => source.Name));
        Assert.All(sources.Statuses, status => Assert.Equal(McpServerState.Connected, status.State));
    }

    [Fact]
    public async Task Reload_ServerAdded_ToolsInNextRefresh()
    {
        AddServer("ha", "intent__HassTurnOn");
        McpToolSources sources = await StartAsync();
        var registry = new ToolRegistry([], [sources]);

        AddServer("search", "query");
        _catalog.Changed();

        await EventuallyAsync(async () => (await registry.RefreshAsync(Ct)).FindByModelName("search_query") is not null);
    }

    [Fact]
    public async Task Reload_ServerRemoved_AllItsToolsDisappear()
    {
        AddServer("ha", "intent__HassTurnOn", "intent__HassTurnOff");
        AddServer("search", "query");
        McpToolSources sources = await StartAsync();
        var registry = new ToolRegistry([], [sources]);
        Assert.Equal(3, (await registry.RefreshAsync(Ct)).Tools.Count);

        _catalog.Definitions.RemoveAll(server => server.Id == "ha");
        _catalog.Changed();

        await EventuallyAsync(async () => (await registry.RefreshAsync(Ct)).Tools.Count == 1);
        Assert.Equal("search.query", Assert.Single(registry.Current.Tools).Id.ToString());
        Assert.Empty(registry.Current.Errors);
    }

    [Fact]
    public async Task Reload_ServerChanged_ReconnectsWithNewDefinition()
    {
        AddServer("search", "query");
        McpToolSources sources = await StartAsync();

        _catalog.Definitions[0] = _catalog.Definitions[0] with { Untrusted = true };
        _catalog.Changed();

        await EventuallyAsync(async () =>
            sources.Statuses.Single().State == McpServerState.Connected
            && ((IToolSourceProvider)sources).Sources.Single() is McpToolSource { Server.Untrusted: true } source
            && (await source.GetToolsAsync(Ct)).Single().Descriptor.UntrustedOutput);
        // At least one reconnect; a slow machine may add a retry of its own.
        Assert.True(_servers["search"].Connections >= 2);
    }

    [Fact]
    public async Task Reload_UnchangedContent_KeepsConnection()
    {
        AddServer("search", "query");
        _catalog.Definitions[0] = _catalog.Definitions[0] with { Arguments = ["--port", "1"] };
        McpToolSources sources = await StartAsync();

        _catalog.Definitions[0] = _catalog.Definitions[0] with { Arguments = ["--port", "1"] };
        _catalog.Changed();
        await sources.WhenIdleAsync();

        Assert.Equal(1, _servers["search"].Connections);
        Assert.Equal(McpServerState.Connected, sources.Statuses.Single().State);
    }

    [Fact]
    public async Task Start_UnreachableServer_OthersWork()
    {
        AddServer("ha", "intent__HassTurnOn");
        AddServer("search", "query");
        _servers["ha"].IsDown = true;

        McpToolSources sources = await StartAsync();
        ToolSnapshot tools = await new ToolRegistry([], [sources]).RefreshAsync(Ct);

        Assert.Equal("search.query", Assert.Single(tools.Tools).Id.ToString());
        Assert.Equal("ha", Assert.Single(tools.Errors).Source);
        Assert.Equal(McpServerState.Down, sources.Statuses.Single(status => status.ServerId == "ha").State);
    }

    [Fact]
    public async Task Dispose_Twice_IsHarmless()
    {
        AddServer("search", "query");
        McpToolSources sources = await StartAsync();
        var source = (McpToolSource)((IToolSourceProvider)sources).Sources.Single();

        await sources.DisposeAsync();
        await sources.DisposeAsync();
        await source.DisposeAsync();

        Assert.Empty(sources.Statuses);
    }

    public async ValueTask DisposeAsync()
    {
        if (_sources is not null)
            await _sources.DisposeAsync();

        foreach (FakeMcpServer server in _servers.Values)
            await server.DisposeAsync();
    }

    private void AddServer(string id, params string[] tools)
    {
        var server = new FakeMcpServer();
        foreach (string tool in tools)
            server.WithTool(tool);

        _servers[id] = server;
        _catalog.Definitions.Add(FakeMcpServerCatalog.Server(id));
    }

    private async Task<McpToolSources> StartAsync()
    {
        _sources = new McpToolSources(_catalog, new Transports(_servers), NullLoggerFactory.Instance, new McpReconnectPolicy(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50)), TimeProvider.System);
        await _sources.StartAsync(TimeSpan.FromSeconds(10), Ct);
        return _sources;
    }

    private static async Task EventuallyAsync(Func<Task<bool>> condition)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (!await HoldsAsync(condition))
        {
            Assert.True(DateTime.UtcNow < deadline, "Condition not met in time.");
            await Task.Delay(10, Ct);
        }
    }

    // A replaced source may still be connecting when the status list already says connected.
    private static async Task<bool> HoldsAsync(Func<Task<bool>> condition)
    {
        try
        {
            return await condition();
        }
        catch (McpServerUnavailableException)
        {
            return false;
        }
    }

    private sealed class Transports(Dictionary<string, FakeMcpServer> servers) : IMcpTransportFactory
    {
        public IClientTransport Create(McpConnectionSettings settings) => servers[settings.Server.Id].CreateTransport();
    }
}
