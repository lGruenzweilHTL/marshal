using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace Marshal.Testing;

/// <summary>
/// In-process MCP server. Every connection runs a fresh server session over in-memory pipes; the server can be
/// taken down, dropped mid-session and asked to announce a changed tool list.
/// </summary>
public sealed class FakeMcpServer : IAsyncDisposable
{
    private readonly Lock _lock = new();
    private Session? _session;

    /// <summary>Tools reported by <c>tools/list</c>; may be changed at any time.</summary>
    public ConcurrentQueue<Tool> Tools { get; } = new();

    /// <summary>Received <c>tools/call</c> requests in order.</summary>
    public ConcurrentQueue<(string Name, JsonElement Arguments)> Calls { get; } = new();

    /// <summary>Answers a call; the default echoes the tool name as text.</summary>
    public Func<string, JsonElement, CallToolResult> Handler { get; set; } =
        (name, _) => new CallToolResult { Content = [new TextContentBlock { Text = $"ok:{name}" }] };

    /// <summary>When true, new connections are refused.</summary>
    public bool IsDown { get; set; }

    /// <summary>Number of accepted connections.</summary>
    public int Connections { get; private set; }

    /// <summary>A client transport that connects to this server.</summary>
    public IClientTransport CreateTransport() => new Transport(this);

    /// <summary>Adds a tool with an object schema.</summary>
    public FakeMcpServer WithTool(string name, string description = "A tool.", string schema = """{"type":"object"}""")
    {
        Tools.Enqueue(Tool(name, description, schema));
        return this;
    }

    /// <summary>A protocol tool.</summary>
    public static Tool Tool(string name, string description = "A tool.", string schema = """{"type":"object"}""") => new()
    {
        Name = name,
        Description = description,
        InputSchema = JsonDocument.Parse(schema).RootElement.Clone(),
    };

    /// <summary>Sends <c>notifications/tools/list_changed</c> on the current session.</summary>
    public async Task NotifyToolsChangedAsync(CancellationToken cancellationToken)
    {
        Session session = _session ?? throw new InvalidOperationException("No client is connected.");
        await session.Server.SendNotificationAsync(NotificationMethods.ToolListChangedNotification, cancellationToken);
    }

    /// <summary>Drops the current session, as if the server process died.</summary>
    public async Task DisconnectAsync()
    {
        Session? session;
        lock (_lock)
        {
            session = _session;
            _session = null;
        }

        if (session is not null)
            await session.DisposeAsync();
    }

    public ValueTask DisposeAsync() => new(DisconnectAsync());

    private McpServerOptions Options() => new()
    {
        ServerInfo = new Implementation { Name = "fake", Version = "1.0" },
        Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = true } },
        Handlers = new McpServerHandlers
        {
            ListToolsHandler = (_, _) => ValueTask.FromResult(new ListToolsResult { Tools = [.. Tools] }),
            CallToolHandler = (request, _) =>
            {
                string name = request.Params!.Name;
                JsonElement arguments = JsonSerializer.SerializeToElement(request.Params.Arguments ?? new Dictionary<string, JsonElement>());
                Calls.Enqueue((name, arguments));
                return ValueTask.FromResult(Handler(name, arguments));
            },
        },
    };

    private sealed class Transport(FakeMcpServer owner) : IClientTransport
    {
        public string Name => "fake";

        public async Task<ITransport> ConnectAsync(CancellationToken cancellationToken = default)
        {
            if (owner.IsDown)
                throw new IOException("Connection refused.");

            await owner.DisconnectAsync();
            var toServer = new Pipe();
            var toClient = new Pipe();
            var server = McpServer.Create(new StreamServerTransport(toServer.Reader.AsStream(), toClient.Writer.AsStream(), "fake"), owner.Options());
            var session = new Session(server, toServer, toClient);
            lock (owner._lock)
            {
                owner._session = session;
                owner.Connections++;
            }

            return await new StreamClientTransport(toServer.Writer.AsStream(), toClient.Reader.AsStream()).ConnectAsync(cancellationToken);
        }
    }

    private sealed class Session : IAsyncDisposable
    {
        private readonly CancellationTokenSource _stop = new();
        private readonly Pipe _toServer;
        private readonly Pipe _toClient;
        private readonly Task _run;

        public Session(McpServer server, Pipe toServer, Pipe toClient)
        {
            Server = server;
            _toServer = toServer;
            _toClient = toClient;
            _run = server.RunAsync(_stop.Token);
        }

        public McpServer Server { get; }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            try
            {
                await _run;
            }
            catch (OperationCanceledException)
            {
            }

            await _toClient.Writer.CompleteAsync();
            await _toServer.Reader.CompleteAsync();
            await Server.DisposeAsync();
            _stop.Dispose();
        }
    }
}
