using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tasks;
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
    /// <summary>Identifier of the MCP tasks extension (SEP-2663).</summary>
    public const string TasksExtension = "io.modelcontextprotocol/tasks";

    /// <summary>
    /// Activity source of the SDK's client spans. While it is listened to, the SDK puts the W3C trace context of
    /// each request's span (a child of the current <c>execute_tool</c> span) into <c>_meta</c>.
    /// </summary>
    public const string TracingSourceName = "Experimental.ModelContextProtocol";

    private readonly McpServerDefinition _server;
    private readonly IMcpServerCatalog _catalog;
    private readonly IMcpTransportFactory _transports;
    private readonly ILoggerFactory _loggers;
    private readonly ILogger _logger;
    private readonly McpReconnectPolicy _reconnect;
    private readonly TimeProvider _time;
    private readonly McpToolPinner? _pinner;
    private readonly Func<IInputRequestQueue?> _inputs;
    private readonly Func<IMcpTaskTracker?> _tasks;
    private readonly CancellationTokenSource _stop = new();
    private readonly TaskCompletionSource _firstAttempt = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Lock _lock = new();
    private Task? _loop;
    private volatile Connection? _connection;
    private volatile State _state;
    private int _disposed;

    public McpToolSource(
        McpServerDefinition server,
        IMcpServerCatalog catalog,
        IMcpTransportFactory transports,
        ILoggerFactory loggers,
        McpReconnectPolicy reconnect,
        TimeProvider time,
        McpToolPinner? pinner = null,
        Func<IInputRequestQueue?>? inputs = null,
        Func<IMcpTaskTracker?>? tasks = null)
    {
        _pinner = server.PinTools ? pinner : null;
        _inputs = inputs ?? (() => null);
        _tasks = tasks ?? (() => null);
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
            return new McpServerStatus(_server.Id, state.ServerState, state.Error, state.Tools.Length) { Warnings = state.Warnings, UnmatchedUntrustedTools = state.UnmatchedUntrustedTools };
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

    internal ToolPinState PinStateOf(string name) => _pinner?.StateOf(_server.Id, name) ?? ToolPinState.Approved;

    internal async Task<Result<ToolOutput>> CallAsync(string name, JsonElement arguments, MarshalExecutionContext context, CancellationToken cancellationToken)
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
        using CallScope scope = connection.Begin(McpInputReplay.Current);
        try
        {
            McpTaskProtocol.CallResult result = await connection.Client.SendRequestAsync<CallToolRequestParams, McpTaskProtocol.CallResult>(
                RequestMethods.ToolsCall, parameters, McpTaskProtocol.Options, cancellationToken: call.Token);
            if (scope.Requested is not null)
                return await RequestInputAsync(name, arguments, scope, context, cancellationToken);
            if (result.IsTask)
                return await TrackAsync(name, result, context, cancellationToken);

            string content = McpContent.ToText(result.ToToolResult());
            return result.IsError == true
                ? new Error("mcp.tool_error", content)
                : new ToolOutput(content);
        }
        catch (Exception) when (scope.Requested is not null)
        {
            // The handler ended the call to ask the user (2026-07-28: its exception; older revisions: the server
            // reports the failed elicitation as a failed call).
            return await RequestInputAsync(name, arguments, scope, context, cancellationToken);
        }
        catch (McpInputUnavailableException exception)
        {
            return new Error("mcp.input_unavailable", exception.Message, ErrorType.Unavailable);
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

    // The request's content is server text: it goes to the web UI only. The model learns that input is needed.
    private async Task<Result<ToolOutput>> RequestInputAsync(string name, JsonElement arguments, CallScope scope, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        ElicitRequestParams request = scope.Requested!;
        IInputRequestQueue? inputs = _inputs();
        if (inputs is null)
            return new Error("mcp.input_unavailable", $"The MCP server '{_server.Id}' needs input from the user, which cannot be asked here.", ErrorType.Unavailable);

        InputRequestMode mode = McpInputAnswer.ModeOf(request);
        InputRequestRecord created = await inputs.CreateAsync(new NewInputRequest(
            _server.Id,
            new ToolId(_server.Namespace, name),
            arguments.ValueKind == JsonValueKind.Object ? arguments.GetRawText() : "{}",
            mode,
            request.Message,
            mode == InputRequestMode.Form && request.RequestedSchema is { } schema ? JsonSerializer.Serialize(schema, McpJsonUtilities.DefaultOptions) : null,
            mode == InputRequestMode.Url ? request.Url : null,
            McpInputAnswer.ToJson(scope.Used)), context, cancellationToken);
        context.Control.RequestInput(created.Id);
        if (scope.Replay is { } replay)
            replay.Created = created;

        return new ToolOutput(InputRequestedText);
    }

    /// <summary>The tool result the model gets while the server waits for the user's input.</summary>
    public const string InputRequestedText =
        "The server needs input from the user before this call can finish; it was asked in the web UI. The call has NOT finished yet. "
        + "Tell the user to answer it in the Marshal web app; the result follows once they do.";

    /// <summary>The tool result the model gets when the server continues the call as a server task.</summary>
    public const string TaskStartedText =
        "The server continues this call in the background as a server task. The call has NOT finished yet. "
        + "Tell the user it is running in the background; the result is delivered when the server reports it done.";

    // A handle nobody follows would run on unnoticed: without a tracker, or past the owner's limit, it is cancelled.
    private async Task<Result<ToolOutput>> TrackAsync(string name, McpTaskProtocol.CallResult handle, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        string taskId = handle.TaskId!;
        IMcpTaskTracker? tracker = _tasks();
        if (tracker is null)
        {
            await CancelTaskAsync(taskId, CancellationToken.None);
            return new Error("mcp.tasks_unavailable", $"The MCP server '{_server.Id}' started a background task, which cannot be followed here; it was cancelled.", ErrorType.Unavailable);
        }

        var start = new McpTaskStart(_server.Id, new ToolId(_server.Namespace, name), taskId, McpTaskProtocol.Interval(handle.PollIntervalMs), IsUntrusted(name) ? $"mcp:{_server.Id}" : null);
        Result<TaskId> tracked = await tracker.TrackAsync(start, context, cancellationToken);
        if (tracked.IsFailure)
        {
            await CancelTaskAsync(taskId, CancellationToken.None);
            return tracked.Error;
        }

        LogTaskStarted(_server.Id, name, taskId, tracked.Value);
        return new ToolOutput(TaskStartedText);
    }

    /// <summary><c>tasks/get</c> on the current connection.</summary>
    public async Task<Result<McpTaskSnapshot>> GetTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        Connection? connection = _connection;
        if (connection is null || connection.IsLost)
            return Unavailable();

        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.LostToken);
        try
        {
            McpTaskProtocol.TaskResult task = await connection.Client.SendRequestAsync<McpTaskProtocol.TaskParams, McpTaskProtocol.TaskResult>(
                McpTaskProtocol.GetMethod, new McpTaskProtocol.TaskParams { TaskId = taskId }, McpTaskProtocol.Options, cancellationToken: call.Token);
            if (McpTaskProtocol.StatusOf(task.Status) is not { } status)
                return new Error("mcp.task_invalid", $"MCP server '{_server.Id}' reported an unknown task status.", ErrorType.Failure);

            TimeSpan? interval = McpTaskProtocol.Interval(task.PollIntervalMs);
            return status switch
            {
                McpTaskStatus.Completed => new McpTaskSnapshot(status, interval, task.Result is { } result ? McpContent.ToText(result) : "", task.Result?.IsError == true),
                McpTaskStatus.Failed => new McpTaskSnapshot(status, interval, Error: task.Error?.Message ?? "The server reported the task failed."),
                McpTaskStatus.InputRequired => new McpTaskSnapshot(status, interval) { Inputs = Inputs(task) },
                _ => new McpTaskSnapshot(status, interval),
            };
        }
        catch (McpProtocolException exception) when (exception.ErrorCode == McpErrorCode.InvalidParams)
        {
            return new Error("mcp.task_unknown", $"MCP server '{_server.Id}' does not know task {taskId}.", ErrorType.NotFound);
        }
        catch (McpProtocolException exception)
        {
            return new Error("mcp.task_error", exception.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            return new Error("mcp.unavailable", $"MCP server '{_server.Id}' did not answer: {exception.Message}", ErrorType.Unavailable);
        }
    }

    // Elicitation only: Marshal offers no sampling or roots, so other requests cannot be answered.
    private static McpTaskInput[] Inputs(McpTaskProtocol.TaskResult task) =>
    [
        .. (task.InputRequests ?? new Dictionary<string, InputRequest>())
            .Where(entry => entry.Value.ElicitationParams is not null)
            .Select(entry =>
            {
                ElicitRequestParams request = entry.Value.ElicitationParams!;
                InputRequestMode mode = McpInputAnswer.ModeOf(request);
                return new McpTaskInput(
                    entry.Key,
                    mode,
                    request.Message,
                    mode == InputRequestMode.Form && request.RequestedSchema is { } schema ? JsonSerializer.Serialize(schema, McpJsonUtilities.DefaultOptions) : null,
                    mode == InputRequestMode.Url ? request.Url : null);
            }),
    ];

    /// <summary><c>tasks/update</c> with one answer on the current connection.</summary>
    public async Task<Abstractions.Result> AnswerInputAsync(string taskId, string inputKey, bool accept, JsonElement? content, CancellationToken cancellationToken)
    {
        Connection? connection = _connection;
        if (connection is null || connection.IsLost)
            return Unavailable();

        var answer = new ElicitResult
        {
            Action = accept ? "accept" : "decline",
            Content = accept && content is { ValueKind: JsonValueKind.Object } values
                ? values.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
                : null,
        };
        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.LostToken);
        try
        {
            await connection.Client.SendRequestAsync<McpTaskProtocol.UpdateParams, McpTaskProtocol.EmptyResult>(
                McpTaskProtocol.UpdateMethod,
                new McpTaskProtocol.UpdateParams { TaskId = taskId, InputResponses = new Dictionary<string, InputResponse> { [inputKey] = InputResponse.FromElicitResult(answer) } },
                McpTaskProtocol.Options,
                cancellationToken: call.Token);
            return Abstractions.Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogUpdateFailed(_server.Id, taskId, exception);
            return new Error("mcp.task_update_failed", $"MCP server '{_server.Id}' did not take the answer for task {taskId}.", ErrorType.Failure);
        }
    }

    /// <summary><c>tasks/cancel</c> on the current connection; best effort.</summary>
    public async Task<Abstractions.Result> CancelTaskAsync(string taskId, CancellationToken cancellationToken)
    {
        Connection? connection = _connection;
        if (connection is null || connection.IsLost)
            return Unavailable();

        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, connection.LostToken);
        try
        {
            await connection.Client.SendRequestAsync<McpTaskProtocol.TaskParams, McpTaskProtocol.EmptyResult>(
                McpTaskProtocol.CancelMethod, new McpTaskProtocol.TaskParams { TaskId = taskId }, McpTaskProtocol.Options, cancellationToken: call.Token);
            return Abstractions.Result.Success();
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCancelFailed(_server.Id, taskId, exception);
            return new Error("mcp.task_cancel_failed", $"MCP server '{_server.Id}' did not cancel task {taskId}.", ErrorType.Failure);
        }
    }

    /// <summary>Description of a server without <c>description</c>: its id and the names of its tools.</summary>
    public static string FallbackDescription(string serverId, IEnumerable<string> toolNames)
    {
        string[] names = [.. toolNames.Order(StringComparer.Ordinal)];
        return names.Length == 0 ? $"Tools of the {serverId} server" : $"Tools of the {serverId} server: {string.Join(", ", names)}";
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

        // ProtocolVersion stays null: the SDK prefers 2026-07-28 and falls back to the newest revision the server
        // supports. No sampling, roots or logging: deprecated in 2026-07-28 and never offered to servers.
        var options = new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "marshal", Version = typeof(McpToolSource).Assembly.GetName().Version?.ToString(3) ?? "0.0.0" },
            Capabilities = new ClientCapabilities
            {
                Elicitation = new ElicitationCapability { Form = new FormElicitationCapability(), Url = new UrlElicitationCapability() },
                Extensions = new Dictionary<string, object> { [TasksExtension] = new JsonObject() },
            },
            Handlers = new McpClientHandlers
            {
                ElicitationHandler = (request, _) => connection.Elicit(request),
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
            Listing listing = await ListAsync(connection.Client, stop);
            _state = Connected(listing.Tools);
            _connection = connection;
            ScheduleRelist(connection, listing.TimeToLive, stop);
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
            Listing listing = await ListAsync(connection.Client, stop);
            if (_connection != connection)
                return;

            _state = Connected(listing.Tools);
            ScheduleRelist(connection, listing.TimeToLive, stop);
        }
        catch (Exception exception) when (!stop.IsCancellationRequested)
        {
            connection.Lost(exception.Message);
        }
    }

    // A positive ttlMs keeps the list fresh until it passes; a change notification re-lists earlier and replaces
    // the timer. Without ttlMs the list is re-read on notifications only.
    private void ScheduleRelist(Connection connection, TimeSpan? timeToLive, CancellationToken stop)
    {
        if (timeToLive is not { } ttl || ttl <= TimeSpan.Zero)
        {
            connection.SetRelistTimer(null);
            return;
        }

        connection.SetRelistTimer(_time.CreateTimer(
            _ => _ = Task.Run(() => RelistAsync(connection, stop), CancellationToken.None),
            null,
            ttl,
            Timeout.InfiniteTimeSpan));
    }

    private async Task<Listing> ListAsync(McpClient client, CancellationToken cancellationToken)
    {
        List<Tool> listed = [];
        TimeSpan? timeToLive = null;
        string? cursor = null;
        do
        {
            ListToolsResult page = await client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, cancellationToken);
            listed.AddRange(page.Tools);
            if (page.TimeToLive is { } ttl)
                timeToLive = timeToLive is { } shortest && shortest < ttl ? shortest : ttl;

            cursor = page.NextCursor;
        }
        while (cursor is not null);

        ImmutableArray<ITool> tools = [.. listed.Select(tool => (ITool)new McpTool(this, Describe(tool), tool.Name))];
        if (_pinner is not null)
            await _pinner.ApplyAsync(_server.Id, listed, cancellationToken);

        return new Listing(tools, timeToLive);
    }

    // A glob that matches no reported tool is likely a typo or a renamed tool: its tools would be trusted silently.
    private State Connected(ImmutableArray<ITool> tools)
    {
        string[] names = [.. tools.OfType<McpTool>().Select(tool => tool.Name)];
        ImmutableArray<string> unmatched = [.. _server.UntrustedTools.Where(glob => !names.Any(name => ToolGlob.Matches(glob, name)))];
        ImmutableArray<string> warnings = [.. unmatched.Select(glob => $"untrustedTools entry '{glob}' matches no tool of MCP server {_server.Id}.")];
        foreach (string warning in warnings)
            LogWarning(_server.Id, warning);

        return new State(McpServerState.Connected, null, tools, warnings) { UnmatchedUntrustedTools = unmatched };
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

    [LoggerMessage(Level = LogLevel.Information, Message = "Tool {Tool} on MCP server {Server} continues as server task {ServerTask}, followed by task {TaskId}")]
    private partial void LogTaskStarted(string server, string tool, string serverTask, TaskId taskId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Answering input of task {ServerTask} on MCP server {Server} failed")]
    private partial void LogUpdateFailed(string server, string serverTask, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Cancelling task {ServerTask} on MCP server {Server} failed")]
    private partial void LogCancelFailed(string server, string serverTask, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Call of tool {Tool} on MCP server {Server} failed")]
    private partial void LogCallFailed(string server, string tool, Exception exception);

    /// <summary>One tool call in flight: the replayed answers it may use and the request it ended with.</summary>
    private sealed class CallScope(Connection connection, McpInputReplay? replay) : IDisposable
    {
        private static readonly AsyncLocal<CallScope?> _current = new();
        private readonly List<McpInputAnswer> _used = [];
        private int _next;

        public static CallScope? Current
        {
            get => _current.Value;
            set => _current.Value = value;
        }

        public McpInputReplay? Replay => replay;

        /// <summary>Answers given to the server so far in this call, in order.</summary>
        public IReadOnlyList<McpInputAnswer> Used => _used;

        /// <summary>The request no stored answer matched; the call ends with it.</summary>
        public ElicitRequestParams? Requested { get; private set; }

        public ElicitResult Answer(ElicitRequestParams request)
        {
            lock (_used)
            {
                IReadOnlyList<McpInputAnswer> answers = replay?.Answers ?? [];
                if (Requested is null && _next < answers.Count && answers[_next].Signature == McpInputAnswer.SignatureOf(request))
                {
                    McpInputAnswer answer = answers[_next++];
                    _used.Add(answer);
                    return new ElicitResult
                    {
                        Action = "accept",
                        Content = answer.Content is { ValueKind: JsonValueKind.Object } content
                            ? content.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone(), StringComparer.Ordinal)
                            : null,
                    };
                }

                Requested ??= request;
            }

            throw new McpInputRequestedException();
        }

        public void Dispose()
        {
            connection.End(this);
            if (_current.Value == this)
                _current.Value = null;
        }
    }

    private sealed record Listing(ImmutableArray<ITool> Tools, TimeSpan? TimeToLive);

    private sealed record State(McpServerState ServerState, string? Error, ImmutableArray<ITool> Tools, ImmutableArray<string> Warnings)
    {
        public ImmutableArray<string> UnmatchedUntrustedTools { get; init; } = [];
    }

    private sealed class Connection : IAsyncDisposable
    {
        private readonly TaskCompletionSource<string> _lost = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationTokenSource _lostToken = new();
        private readonly ConcurrentDictionary<CallScope, byte> _calls = new();
        private McpClient? _client;
        private ITimer? _relist;

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
            if (!_lost.TrySetResult(reason))
                return;

            _lostToken.Cancel();
            Interlocked.Exchange(ref _relist, null)?.Dispose();
        }

        /// <summary>Tracks a call so the elicitation handler finds it.</summary>
        public CallScope Begin(McpInputReplay? replay)
        {
            var scope = new CallScope(this, replay);
            _calls[scope] = 0;
            CallScope.Current = scope;
            return scope;
        }

        public void End(CallScope scope) => _calls.TryRemove(scope, out _);

        // Never waits for a person: an input request is answered from a replay's stored answers, or recorded on the
        // call and the call ends. On 2026-07-28 the request arrives in the caller's async flow; older revisions send
        // it on the connection, so it belongs to the only call in flight (with several, nobody can tell which).
        public ValueTask<ElicitResult> Elicit(ElicitRequestParams? request)
        {
            if (request is null)
                throw new McpInputUnavailableException("The server sent an empty input request.");

            CallScope? scope = CallScope.Current is { } current && _calls.ContainsKey(current) ? current
                : _calls.Count == 1 ? _calls.Keys.Single()
                : null;
            if (scope is null)
                throw new McpInputUnavailableException("The server asked for input, but the call it belongs to is unclear.");

            return ValueTask.FromResult(scope.Answer(request));
        }

        /// <summary>Replaces the pending ttl re-list, if any.</summary>
        public void SetRelistTimer(ITimer? timer)
        {
            Interlocked.Exchange(ref _relist, timer)?.Dispose();
            if (IsLost)
                Interlocked.Exchange(ref _relist, null)?.Dispose();
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
