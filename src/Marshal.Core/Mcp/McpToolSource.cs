using System.Collections.Immutable;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Marshal.Core.Mcp;

/// <summary>
/// Tools of one configured MCP server. A background loop keeps the connection: it connects, discovers the
/// tools, re-lists them on <c>tools/list_changed</c>, and reconnects with backoff after a disconnect or a failed
/// call. While the server is down its tools are hidden (<see cref="GetToolsAsync"/> throws, so the registry
/// rejects the source for that refresh and keeps every other source).
/// </summary>
public sealed partial class McpToolSource : IToolSource, IAsyncDisposable
{
    private readonly McpServerDefinition _server;
    private readonly IMcpServerCatalog _catalog;
    private readonly IMcpTransportFactory _transports;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _logger;
    private readonly McpReconnectPolicy _reconnect;
    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _firstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _lock = new();
    private Task? _loop;
    private volatile Connection? _connection;
    private volatile State _state;
    private int _disposed;

    public McpToolSource(McpServerDefinition server, IMcpServerCatalog catalog, IMcpTransportFactory transports, ILoggerFactory loggers, McpReconnectPolicy reconnect, TimeProvider time)
    {
        _server = server;
        _catalog = catalog;
        _transports = transports;
        _loggers = loggers;
        _logger = loggers.CreateLogger<McpToolSource>();
        _reconnect = reconnect;
        _time = time;
        _state = new State(McpServerState.Connecting, null, [], []);
    }

    /// <summary>The server id.</summary>
    public string Name => _server.Id;

    public bool IsBuiltIn => false;

    /// <summary>The server's namespace with its configured description, or one built from the id and tool names.</summary>
    public IReadOnlyList<ToolNamespace> Namespaces =>
        [new(_server.Namespace, _server.Description ?? FallbackDescription(_server.Id, _state.Tools.OfType<McpTool>().Select(tool => tool.Name)))];

    /// <summary>The definition this source was created from.</summary>
    public McpServerDefinition Server => _server;

    /// <summary>Current connection state.</summary>
    public McpServerStatus Status
    {
        get
        {
            State state = _state;
            return new McpServerStatus(_server.Id, state.ServerState, state.Error, state.Tools.Length) { Warnings = state.Warnings };
        }
    }

    /// <summary>Starts the connection loop and waits for the first connection attempt to finish (either way).</summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        lock (_lock)
            _loop ??= Task.Run(() => RunAsync(_stop.Token), CancellationToken.None);

        return _firstAttempt.Task.WaitAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken)
    {
        State state = _state;
        if (state.ServerState != McpServerState.Connected)
            throw new McpServerUnavailableException($"MCP server '{_server.Id}' is unavailable: {state.Error ?? "not connected yet"}.");

        return Task.FromResult<IReadOnlyList<ITool>>(state.Tools);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
            return;

        await _stop.CancelAsync();
        if (_loop is not null)
            await _loop;

        _stop.Dispose();
    }

    internal async Task<Result<ToolOutput>> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        Connection? connection = _connection;
        if (connection is null || connection.IsLost)
            return Unavailable();

        var parameters = new CallToolRequestParams
        {
            Name = name,
            Arguments = arguments.ValueKind == JsonValueKind.Object
                ? arguments.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
                : new Dictionary<string, JsonElement>(StringComparer.Ordinal),
        };

        // A request sent just as the connection drops may never be answered; losing the connection ends it.
        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.LostToken);
        try
        {
            CallToolResult result = await connection.Client.CallToolAsync(parameters, call.Token);
            string content = McpContent.ToText(result);
            return result.IsError == true
                ? new Error("mcp.tool_error", content)
                : new ToolOutput(content);
        }
        catch (McpProtocolException exception)
        {
            return new Error("mcp.tool_error", exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCallFailed(_server.Id, name, exception);
            connection.Lost(exception.Message);
            return Unavailable();
        }
    }

    private Error Unavailable() => new("mcp.unavailable", $"MCP server '{_server.Id}' is unavailable.", ErrorType.Unavailable);

    private async Task RunAsync(CancellationToken stop)
    {
        int attempt = 0;
        while (!stop.IsCancellationRequested)
        {
            Connection? connection = null;
            try
            {
                connection = await ConnectAsync(stop);
                if (connection is not null)
                {
                    attempt = 0;
                    _firstAttempt.TrySetResult();
                    string reason = await connection.WaitUntilLostAsync(stop);
                    SetDown(reason);
                }
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                SetDown(exception.Message);
            }
            finally
            {
                _connection = null;
                if (connection is not null)
                    await connection.DisposeAsync();
            }

            _firstAttempt.TrySetResult();
            try
            {
                await Task.Delay(_reconnect.Delay(attempt++), _time, stop);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _state = new State(McpServerState.Down, "Stopped", [], []);
        _firstAttempt.TrySetResult();
    }

    private async Task<Connection?> ConnectAsync(CancellationToken stop)
    {
        Result<McpConnectionSettings> settings = await _catalog.ResolveAsync(_server, stop);
        if (settings.IsFailure)
        {
            SetDown(settings.Error.Message);
            return null;
        }

        var connection = new Connection();
        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "marshal", Version = typeof(McpToolSource).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" },
            Handlers = new McpClientHandlers
            {
                NotificationHandlers =
                [
                    new(NotificationMethods.ToolListChangedNotification, (_, _) =>
                    {
                        _ = Task.Run(() => RelistAsync(connection, stop), CancellationToken.None);
                        return ValueTask.CompletedTask;
                    }),
                ],
            },
        };

        try
        {
            connection.Client = await McpClient.CreateAsync(_transports.Create(settings.Value), options, _loggers, stop);
            _state = Connected(await ListAsync(connection.Client, stop));
            _connection = connection;
            LogConnected(_server.Id, _state.Tools.Length);
            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private async Task RelistAsync(Connection connection, CancellationToken stop)
    {
        try
        {
            ImmutableArray<ITool> tools = await ListAsync(connection.Client, stop);
            if (_connection == connection)
                _state = Connected(tools);
        }
        catch (Exception exception) when (!stop.IsCancellationRequested)
        {
            connection.Lost(exception.Message);
        }
    }

    private async Task<ImmutableArray<ITool>> ListAsync(McpClient client, CancellationToken cancellationToken)
    {
        IList<McpClientTool> listed = await client.ListToolsAsync(cancellationToken: cancellationToken);
        return [.. listed.Select(tool => (ITool)new McpTool(this, Describe(tool.ProtocolTool), tool.ProtocolTool.Name))];
    }

    // A glob that matches no reported tool is likely a typo or a renamed tool: its tools would be trusted silently.
    private State Connected(ImmutableArray<ITool> tools)
    {
        string[] names = [.. tools.OfType<McpTool>().Select(tool => tool.Name)];
        ImmutableArray<string> warnings = [.. _server.UntrustedTools
            .Where(glob => !names.Any(name => ToolGlob.Matches(glob, name)))
            .Select(glob => $"untrustedTools entry '{glob}' matches no tool of MCP server {_server.Id}.")];
        foreach (string warning in warnings)
            LogWarning(_server.Id, warning);

        return new State(McpServerState.Connected, null, tools, warnings);
    }

    private bool IsUntrusted(string name) => _server.Untrusted || _server.UntrustedTools.Any(glob => ToolGlob.Matches(glob, name));

    private ToolDescriptor Describe(Tool tool)
    {
        if (string.IsNullOrWhiteSpace(tool.Name))
            throw new InvalidDataException($"MCP server '{_server.Id}' reported a tool without a name.");

        return new ToolDescriptor
        {
            Id = new ToolId(_server.Namespace, tool.Name),
            Description = tool.Description ?? tool.Title ?? string.Empty,
            ParametersSchema = tool.InputSchema,
            DefaultTier = RiskTier.Confirm,
            UntrustedOutput = IsUntrusted(tool.Name),
            TaintKind = IsUntrusted(tool.Name) ? $"mcp:{_server.Id}" : null,
        };
    }

    private void SetDown(string reason)
    {
        _state = new State(McpServerState.Down, reason, [], []);
        LogDown(_server.Id, reason);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "MCP server {Server} connected with {Count} tools")]
    private partial void LogConnected(string server, int count);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP server {Server}: {Warning}")]
    private partial void LogWarning(string server, string warning);

    [LoggerMessage(Level = LogLevel.Warning, Message = "MCP server {Server} is down: {Reason}")]
    private partial void LogDown(string server, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Call of tool {Tool} on MCP server {Server} failed")]
    private partial void LogCallFailed(string server, string tool, Exception exception);

    private sealed record State(McpServerState ServerState, string? Error, ImmutableArray<ITool> Tools, ImmutableArray<string> Warnings);

    private sealed class Connection : IAsyncDisposable
    {
        private readonly TaskCompletionSource<string> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _lostToken = new();
        private McpClient? _client;

        public McpClient Client
        {
            get => _client ?? throw new InvalidOperationException("Not connected.");
            set
            {
                _client = value;
                _ = value.Completion.ContinueWith(
                    completion => Lost(completion.IsCompletedSuccessfully ? completion.Result.Exception?.Message ?? "Disconnected" : "Disconnected"),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
        }

        public bool IsLost => _lost.Task.IsCompleted;

        /// <summary>Cancelled once the connection is lost.</summary>
        public CancellationToken LostToken => _lostToken.Token;

        public void Lost(string reason)
        {
            if (_lost.TrySetResult(reason))
                _lostToken.Cancel();
        }

        public Task<string> WaitUntilLostAsync(CancellationToken cancellationToken) => _lost.Task.WaitAsync(cancellationToken);

        public async ValueTask DisposeAsync()
        {
            Lost("Closed");
            if (_client is not null)
                await _client.DisposeAsync();

            // _lostToken is not disposed: a call may still link to it; it holds no timer or handle.
        }
    }
}
