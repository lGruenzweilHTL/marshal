using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Marshal.Core.Mcp;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;

namespace Marshal.Core.Tests.Mcp;

public sealed class McpTransportFactoryTests : IAsyncDisposable
{
    private static readonly McpServerDefinition _ha = new()
    {
        Id = "ha",
        Namespace = "ha",
        Transport = McpTransportKind.Http,
        Url = new Uri("http://homeassistant.local:8123/api/mcp"),
        File = "ha.yaml",
    };

    private readonly ReplayHandler _handler = new();
    private readonly List<McpToolSource> _sources = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Http_RecordedHomeAssistantSession_DiscoversAndCallsTools()
    {
        McpToolSource source = await StartAsync(new Dictionary<string, string> { ["Authorization"] = "Bearer <token>" });

        IReadOnlyList<ITool> tools = await source.GetToolsAsync(Ct);
        ITool turnOff = Assert.Single(tools, tool => tool.Descriptor.Id == new ToolId("ha", "intent__HassTurnOff"));
        Assert.Contains(tools, tool => tool.Descriptor.Id == new ToolId("ha", "homeassistant__GetLiveContext"));

        Result<ToolOutput> result = await turnOff.InvokeAsync(new ToolInvocation(JsonDocument.Parse("""{"area":"Hall","domain":["light"]}""").RootElement, TestExecutionContext.Text()), Ct);

        Assert.Contains("action_done", result.Value.Content, StringComparison.Ordinal);
        Assert.All(_handler.Requests, request => Assert.Equal("Bearer <token>", request.Authorization));
        Assert.All(_handler.Requests, request => Assert.Equal("/api/mcp", request.Path));
    }

    [Fact]
    public async Task Http_WithoutToken_ServerIsDown()
    {
        McpToolSource source = await StartAsync(new Dictionary<string, string>());

        Assert.Equal(McpServerState.Down, source.Status.State);
        Assert.NotNull(source.Status.Error);
        Assert.Equal(0, source.Status.ToolCount);
    }

    [Fact]
    public void Stdio_ChildEnvironment_HasOnlyBasicsAndConfiguredSecrets()
    {
        var parent = new Dictionary<string, string?>
        {
            ["PATH"] = "/usr/bin",
            ["HOME"] = "/home/marshal",
            ["LANG"] = "C.UTF-8",
            ["MARSHAL__Secrets__anthropic_key"] = "sk-secret",
            ["ANTHROPIC_API_KEY"] = "sk-other",
        };
        var settings = new McpConnectionSettings(
            _ha with { Transport = McpTransportKind.Stdio, Url = null, Command = "search-mcp" },
            new Dictionary<string, string>(),
            new Dictionary<string, string> { ["SEARCH_API_KEY"] = "search-secret" });

        IReadOnlyDictionary<string, string?> environment = McpTransportFactory.ChildEnvironment(settings, parent);

        Assert.Equal(
            new Dictionary<string, string?> { ["PATH"] = "/usr/bin", ["HOME"] = "/home/marshal", ["LANG"] = "C.UTF-8", ["SEARCH_API_KEY"] = "search-secret" },
            environment);
    }

    [Fact]
    public void Create_TransportNamedAfterServer()
    {
        var factory = new McpTransportFactory(new SingleClientFactory(_handler), NullLoggerFactory.Instance);

        IClientTransport http = factory.Create(new McpConnectionSettings(_ha, new Dictionary<string, string>(), new Dictionary<string, string>()));
        IClientTransport stdio = factory.Create(new McpConnectionSettings(_ha with { Id = "search", Transport = McpTransportKind.Stdio, Url = null, Command = "search-mcp" }, new Dictionary<string, string>(), new Dictionary<string, string>()));

        Assert.IsType<HttpClientTransport>(http);
        Assert.Equal("ha", http.Name);
        Assert.IsType<StdioClientTransport>(stdio);
        Assert.Equal("search", stdio.Name);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (McpToolSource source in _sources)
            await source.DisposeAsync();
    }

    private async Task<McpToolSource> StartAsync(Dictionary<string, string> headers)
    {
        var catalog = new SettingsCatalog(headers);
        var factory = new McpTransportFactory(new SingleClientFactory(_handler), NullLoggerFactory.Instance);
        var source = new McpToolSource(_ha, catalog, factory, NullLoggerFactory.Instance, new McpReconnectPolicy(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1)), TimeProvider.System);
        _sources.Add(source);
        await source.StartAsync(Ct);
        return source;
    }

    private sealed class SettingsCatalog(Dictionary<string, string> headers) : IMcpServerCatalog
    {
        public IReadOnlyList<McpServerDefinition> Servers => [_ha];

        public IReadOnlyList<Abstractions.Configuration.ConfigFileError> Errors => [];

        public IDisposable OnChange(Action listener) => throw new NotSupportedException();

        public Task<Result<McpConnectionSettings>> ResolveAsync(McpServerDefinition server, CancellationToken cancellationToken) =>
            Task.FromResult<Result<McpConnectionSettings>>(new McpConnectionSettings(server, headers, new Dictionary<string, string>()));
    }

    private sealed class SingleClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>Answers like Home Assistant's stateless <c>/api/mcp</c>, from the recorded session.</summary>
    private sealed class ReplayHandler : HttpMessageHandler
    {
        private readonly JsonArray _exchanges = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "mcp-session.json")))!.AsArray();

        public ConcurrentQueue<(string Path, string? Authorization)> Requests { get; } = new();

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.Method != HttpMethod.Post)
                return new HttpResponseMessage(HttpStatusCode.MethodNotAllowed);

            string? authorization = request.Headers.Authorization?.ToString();
            Requests.Enqueue((request.RequestUri!.AbsolutePath, authorization));
            if (authorization is null)
                return new HttpResponseMessage(HttpStatusCode.Unauthorized) { Content = new StringContent("401: Unauthorized") };

            JsonNode message = JsonNode.Parse(await request.Content!.ReadAsStringAsync(cancellationToken))!;
            if (message["id"] is null)
                return new HttpResponseMessage(HttpStatusCode.Accepted);

            JsonNode exchange = _exchanges.FirstOrDefault(candidate => Matches(candidate!["request"]!, message) && (int)candidate["status"]! == 200) ?? throw new InvalidOperationException("Unrecorded request: " + message.ToJsonString());
            JsonNode response = exchange["response"]!.DeepClone();
            response["id"] = message["id"]!.DeepClone();
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(response.ToJsonString(), Encoding.UTF8, "application/json") };
        }

        private static bool Matches(JsonNode recorded, JsonNode message) =>
            (string?)recorded["method"] == (string?)message["method"]
            && ((string?)message["method"] != "tools/call" || JsonNode.DeepEquals(recorded["params"], message["params"]));
    }
}
