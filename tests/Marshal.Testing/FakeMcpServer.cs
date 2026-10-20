#pragma warning disable MCPEXP002 // Server tasks need the experimental alternate-result and custom request handlers.

using System.Collections.Concurrent;
using System.IO.Pipelines;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using ModelContextProtocol;
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
    private static readonly JsonTypeInfo<TaskHandle> _taskHandleType = (JsonTypeInfo<TaskHandle>)new JsonSerializerOptions { TypeInfoResolver = new DefaultJsonTypeInfoResolver() }.GetTypeInfo(typeof(TaskHandle));
    private int _listCalls;
    private int _taskCount;

    /// <summary>Tools reported by <c>tools/list</c>; may be changed at any time.</summary>
    public ConcurrentQueue<Tool> Tools { get; } = new();

    /// <summary>Received <c>tools/call</c> requests in order.</summary>
    public ConcurrentQueue<(string Name, JsonElement Arguments)> Calls { get; } = new();

    /// <summary>Answers a call; the default echoes the tool name as text.</summary>
    public Func<string, JsonElement, CallToolResult> Handler { get; set; } =
        (name, _) => new CallToolResult { Content = [new TextContentBlock { Text = $"ok:{name}" }] };

    /// <summary>Protocol revision the server is pinned to; null supports every revision the SDK knows.</summary>
    public string? ProtocolVersion { get; set; }

    /// <summary><c>ttlMs</c> sent with every <c>tools/list</c> result; null omits it.</summary>
    public TimeSpan? ListTimeToLive { get; set; }

    /// <summary>Number of <c>tools/list</c> requests received.</summary>
    public int ListCalls => _listCalls;

    /// <summary>Capabilities the client declared with its last <c>tools/list</c> request.</summary>
    public ClientCapabilities? ClientCapabilities { get; private set; }

    /// <summary>Protocol revision of the last <c>tools/list</c> request.</summary>
    public string? NegotiatedProtocolVersion { get; private set; }

    /// <summary>
    /// Answers a call with the full request (input responses and request state included); takes precedence over
    /// <see cref="Handler"/>. Throw <see cref="InputRequiredException"/> to ask the client for input.
    /// </summary>
    public Func<CallToolRequestParams, CallToolResult>? RequestHandler { get; set; }

    /// <summary>Received <c>tools/call</c> requests with their parameters, in order.</summary>
    public ConcurrentQueue<CallToolRequestParams> CallRequests { get; } = new();

    /// <summary>
    /// Tools whose calls return a task handle (tasks extension) instead of a result, with the poll interval the
    /// handle suggests (null omits it).
    /// </summary>
    public ConcurrentDictionary<string, TimeSpan?> TaskTools { get; } = new();

    /// <summary>
    /// Server tasks by id, as <c>tasks/get</c> reports them (<c>status</c> and <c>result</c> or <c>error</c>);
    /// set an entry to finish a task. Unknown ids answer with an invalid-params error.
    /// </summary>
    public ConcurrentDictionary<string, JsonObject> ServerTasks { get; } = new();

    /// <summary>Received <c>tasks/get</c> task ids in order.</summary>
    public ConcurrentQueue<string> TaskPolls { get; } = new();

    /// <summary>Received <c>tasks/update</c> requests (task id and <c>inputResponses</c>) in order.</summary>
    public ConcurrentQueue<(string TaskId, JsonObject InputResponses)> TaskUpdates { get; } = new();

    /// <summary>Received <c>tasks/cancel</c> task ids in order.</summary>
    public ConcurrentQueue<string> CancelledTasks { get; } = new();

    /// <summary>Resources served by <c>resources/read</c> by URI (MIME type, text, optional <c>ttlMs</c>).</summary>
    public ConcurrentDictionary<string, (string MimeType, string Text, TimeSpan? TimeToLive)> Resources { get; } = new(StringComparer.Ordinal);

    /// <summary>Received <c>resources/read</c> URIs in order.</summary>
    public ConcurrentQueue<string> ResourceReads { get; } = new();

    /// <summary>Adds a tool whose MCP Apps metadata names <paramref name="resourceUri"/>.</summary>
    public FakeMcpServer WithAppTool(string name, string resourceUri)
    {
        Tool tool = Tool(name);
        tool.Meta = new JsonObject { ["ui"] = new JsonObject { ["resourceUri"] = resourceUri } };
        Tools.Enqueue(tool);
        return this;
    }

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
        ProtocolVersion = ProtocolVersion,
        Capabilities = new ServerCapabilities
        {
            Tools = new ToolsCapability { ListChanged = true },
            Resources = new ResourcesCapability(),
            Extensions = new Dictionary<string, object> { ["io.modelcontextprotocol/tasks"] = new JsonObject() },
        },
        Handlers = new McpServerHandlers
        {
            ListToolsHandler = (request, _) =>
            {
                Interlocked.Increment(ref _listCalls);
                ClientCapabilities = request.Server.ClientCapabilities;
                NegotiatedProtocolVersion = request.Server.NegotiatedProtocolVersion;
                return ValueTask.FromResult(new ListToolsResult { Tools = [.. Tools], TimeToLive = ListTimeToLive });
            },
            ReadResourceHandler = (request, _) =>
            {
                string uri = request.Params!.Uri;
                ResourceReads.Enqueue(uri);
                if (!Resources.TryGetValue(uri, out (string MimeType, string Text, TimeSpan? TimeToLive) resource))
                    throw new McpProtocolException($"Unknown resource {uri}.", McpErrorCode.InvalidParams);

                return ValueTask.FromResult(new ReadResourceResult
                {
                    Contents = [new TextResourceContents { Uri = uri, MimeType = resource.MimeType, Text = resource.Text }],
                    TimeToLive = resource.TimeToLive,
                });
            },
            CallToolWithAlternateHandler = (request, _) =>
            {
                string name = request.Params!.Name;
                JsonElement arguments = JsonSerializer.SerializeToElement(request.Params.Arguments ?? new Dictionary<string, JsonElement>());
                Calls.Enqueue((name, arguments));
                CallRequests.Enqueue(request.Params);
                if (TaskTools.TryGetValue(name, out TimeSpan? interval))
                {
                    string id = $"task-{Interlocked.Increment(ref _taskCount)}";
                    ServerTasks[id] = new JsonObject { ["taskId"] = id, ["status"] = "working" };
                    var handle = new TaskHandle { ResultType = "task", TaskId = id, Status = "working", PollIntervalMs = (long?)interval?.TotalMilliseconds };
                    return ValueTask.FromResult(ResultOrAlternate<CallToolResult>.FromAlternate(handle, _taskHandleType));
                }

                return ValueTask.FromResult<ResultOrAlternate<CallToolResult>>(RequestHandler is { } raw ? raw(request.Params) : Handler(name, arguments));
            },
        },
        RequestHandlers =
        [
            new McpServerRequestHandler { Method = "tasks/get", Handler = (request, _) => ValueTask.FromResult<JsonNode?>(GetTask(request)) },
            new McpServerRequestHandler { Method = "tasks/update", Handler = (request, _) => ValueTask.FromResult<JsonNode?>(UpdateTask(request)) },
            new McpServerRequestHandler { Method = "tasks/cancel", Handler = (request, _) => ValueTask.FromResult<JsonNode?>(CancelTask(request)) },
        ],
    };

    /// <summary>Finishes a server task with a text result.</summary>
    public void CompleteTask(string id, string text, bool isError = false) => ServerTasks[id] = new JsonObject
    {
        ["taskId"] = id,
        ["status"] = "completed",
        ["result"] = new JsonObject { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }), ["isError"] = isError },
    };

    /// <summary>Ends a server task with a JSON-RPC error.</summary>
    public void FailTask(string id, string message) => ServerTasks[id] = new JsonObject
    {
        ["taskId"] = id,
        ["status"] = "failed",
        ["error"] = new JsonObject { ["code"] = -32603, ["message"] = message },
    };

    /// <summary>Makes a server task wait for input (<c>input_required</c> with elicitation requests by key).</summary>
    public void AskInTask(string id, IReadOnlyDictionary<string, ElicitRequestParams> requests)
    {
        var inputs = new JsonObject();
        foreach ((string key, ElicitRequestParams request) in requests)
            inputs[key] = JsonSerializer.SerializeToNode(InputRequest.ForElicitation(request), McpJsonUtilities.DefaultOptions);

        ServerTasks[id] = new JsonObject { ["taskId"] = id, ["status"] = "input_required", ["inputRequests"] = inputs };
    }

    private JsonObject UpdateTask(JsonRpcRequest request)
    {
        string id = TaskIdOf(request);
        TaskUpdates.Enqueue((id, request.Params?["inputResponses"]?.DeepClone() as JsonObject ?? []));
        if (ServerTasks.TryGetValue(id, out JsonObject? task))
        {
            task["status"] = "working";
            task.Remove("inputRequests");
        }

        return new JsonObject { ["resultType"] = "complete" };
    }

    private JsonObject GetTask(JsonRpcRequest request)
    {
        string id = TaskIdOf(request);
        TaskPolls.Enqueue(id);
        if (!ServerTasks.TryGetValue(id, out JsonObject? task))
            throw new McpProtocolException($"Unknown task {id}.", McpErrorCode.InvalidParams);

        var result = (JsonObject)task.DeepClone();
        result["resultType"] = "complete";
        return result;
    }

    private JsonObject CancelTask(JsonRpcRequest request)
    {
        string id = TaskIdOf(request);
        CancelledTasks.Enqueue(id);
        if (ServerTasks.TryGetValue(id, out JsonObject? task))
            task["status"] = "cancelled";

        return new JsonObject { ["resultType"] = "complete" };
    }

    private static string TaskIdOf(JsonRpcRequest request) =>
        request.Params?["taskId"]?.GetValue<string>() ?? throw new McpProtocolException("taskId is required.", McpErrorCode.InvalidParams);

    /// <summary>A <c>CreateTaskResult</c> (<c>resultType: task</c>).</summary>
    private sealed class TaskHandle : Result
    {
        [JsonPropertyName("taskId")]
        public required string TaskId { get; init; }

        [JsonPropertyName("status")]
        public required string Status { get; init; }

        [JsonPropertyName("pollIntervalMs")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public long? PollIntervalMs { get; init; }
    }

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
