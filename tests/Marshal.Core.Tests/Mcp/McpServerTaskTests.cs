using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Mcp;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ElicitRequestParams = ModelContextProtocol.Protocol.ElicitRequestParams;

namespace Marshal.Core.Tests.Mcp;

public sealed class McpServerTaskTests : IAsyncDisposable
{
    private readonly FakeMcpServer _server = new();
    private readonly FakeMcpServerCatalog _catalog = new();
    private readonly RecordingTracker _tracker = new();
    private readonly List<McpToolSource> _sources = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Revisions => ["2026-07-28", "2025-11-25"];

    [Theory]
    [MemberData(nameof(Revisions))]
    public async Task TaskHandle_TrackedWithTheCallsContext_ToolResultSaysTheWorkContinues(string revision)
    {
        _server.ProtocolVersion = revision;
        _server.WithTool("export_report");
        _server.TaskTools["export_report"] = TimeSpan.FromSeconds(20);
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports"));
        MarshalExecutionContext context = TestExecutionContext.Voice();

        Result<ToolOutput> result = await InvokeAsync(source, context);

        Assert.True(result.IsSuccess);
        Assert.Equal(McpToolSource.TaskStartedText, result.Value.Content);
        (McpTaskStart start, MarshalExecutionContext tracked) = Assert.Single(_tracker.Started);
        Assert.Equal(new McpTaskStart("reports", new ToolId("reports", "export_report"), "task-1", TimeSpan.FromSeconds(20), null), start);
        Assert.Same(context, tracked);
        Assert.Empty(_server.CancelledTasks);
    }

    [Fact]
    public async Task UntrustedTool_HandleCarriesItsTaintKind()
    {
        _server.WithTool("export_report");
        _server.TaskTools["export_report"] = null;
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports", untrusted: true));

        await InvokeAsync(source, TestExecutionContext.Text());

        McpTaskStart start = Assert.Single(_tracker.Started).Start;
        Assert.Equal(("mcp:reports", (TimeSpan?)null), (start.TaintKind, start.PollInterval));
    }

    [Fact]
    public async Task TrackerRefuses_ServerTaskIsCancelled_ToolResultSaysWhy()
    {
        _server.WithTool("export_report");
        _server.TaskTools["export_report"] = null;
        _tracker.Refusal = new Error("mcp.task_limit", "Too many server tasks are running; try again later.", ErrorType.Conflict);
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports"));

        Result<ToolOutput> result = await InvokeAsync(source, TestExecutionContext.Text());

        Assert.Equal("mcp.task_limit", result.Error.Code);
        Assert.Equal(["task-1"], _server.CancelledTasks);
    }

    [Fact]
    public async Task NoTracker_ServerTaskIsCancelled_Unavailable()
    {
        _server.WithTool("export_report");
        _server.TaskTools["export_report"] = null;
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports"), withTracker: false);

        Result<ToolOutput> result = await InvokeAsync(source, TestExecutionContext.Text());

        Assert.Equal(("mcp.tasks_unavailable", ErrorType.Unavailable), (result.Error.Code, result.Error.Type));
        Assert.Equal(["task-1"], _server.CancelledTasks);
    }

    [Fact]
    public async Task OrdinaryResult_NotTracked()
    {
        _server.WithTool("export_report");
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports"));

        Result<ToolOutput> result = await InvokeAsync(source, TestExecutionContext.Text());

        Assert.Equal("ok:export_report", result.Value.Content);
        Assert.Empty(_tracker.Started);
    }

    [Theory]
    [MemberData(nameof(Revisions))]
    public async Task GetTask_ReportsWorkingCompletedAndFailed(string revision)
    {
        _server.ProtocolVersion = revision;
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports"));
        _server.ServerTasks["a"] = new() { ["taskId"] = "a", ["status"] = "working", ["pollIntervalMs"] = 30000 };
        _server.CompleteTask("b", "Report ready: q3.pdf");
        _server.FailTask("c", "Disk full");

        Result<McpTaskSnapshot> working = await source.GetTaskAsync("a", Ct);
        Result<McpTaskSnapshot> completed = await source.GetTaskAsync("b", Ct);
        Result<McpTaskSnapshot> failed = await source.GetTaskAsync("c", Ct);
        Result<McpTaskSnapshot> unknown = await source.GetTaskAsync("d", Ct);

        Assert.Equal(new McpTaskSnapshot(McpTaskStatus.Working, TimeSpan.FromSeconds(30)), working.Value);
        Assert.Equal(new McpTaskSnapshot(McpTaskStatus.Completed, Result: "Report ready: q3.pdf"), completed.Value);
        Assert.Equal(new McpTaskSnapshot(McpTaskStatus.Failed, Error: "Disk full"), failed.Value);
        Assert.Equal(ErrorType.NotFound, unknown.Error.Type);
    }

    [Theory]
    [MemberData(nameof(Revisions))]
    public async Task GetTask_InputRequired_ReportsTheElicitationRequests(string revision)
    {
        _server.ProtocolVersion = revision;
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports"));
        _server.AskInTask("a", new Dictionary<string, ElicitRequestParams>
        {
            ["q1"] = new() { Mode = "form", Message = "Which quarter?", RequestedSchema = new() { Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { ["quarter"] = new ElicitRequestParams.NumberSchema() } } },
            ["q2"] = new() { Mode = "url", Message = "Log in to the report service.", Url = "https://reports.example/login", ElicitationId = "e1" },
        });

        McpTaskSnapshot snapshot = (await source.GetTaskAsync("a", Ct)).Value;

        Assert.Equal(McpTaskStatus.InputRequired, snapshot.Status);
        Assert.Equal(["q1", "q2"], snapshot.Inputs.Select(input => input.Key).Order(StringComparer.Ordinal));
        McpTaskInput form = snapshot.Inputs.Single(input => input.Key == "q1");
        Assert.Equal((InputRequestMode.Form, "Which quarter?", (string?)null), (form.Mode, form.Message, form.Url));
        Assert.Contains("\"quarter\"", form.RequestedSchemaJson, StringComparison.Ordinal);
        McpTaskInput url = snapshot.Inputs.Single(input => input.Key == "q2");
        Assert.Equal((InputRequestMode.Url, "https://reports.example/login", (string?)null), (url.Mode, url.Url, url.RequestedSchemaJson));
    }

    [Fact]
    public async Task AnswerInput_SendsTasksUpdate()
    {
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports"));
        _server.ServerTasks["a"] = new() { ["taskId"] = "a", ["status"] = "input_required" };

        Result accepted = await source.AnswerInputAsync("a", "q1", accept: true, JsonDocument.Parse("""{"quarter":3}""").RootElement.Clone(), Ct);
        Result declined = await source.AnswerInputAsync("a", "q2", accept: false, null, Ct);

        Assert.True(accepted.IsSuccess && declined.IsSuccess);
        (string TaskId, JsonObject Responses)[] updates = [.. _server.TaskUpdates];
        Assert.Equal(("a", "accept", 3), (updates[0].TaskId, updates[0].Responses["q1"]!["action"]!.GetValue<string>(), updates[0].Responses["q1"]!["content"]!["quarter"]!.GetValue<int>()));
        Assert.Equal("decline", updates[1].Responses["q2"]!["action"]!.GetValue<string>());
    }

    [Fact]
    public async Task GetTask_ServerDown_Unavailable()
    {
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports"));
        _server.IsDown = true;
        await _server.DisconnectAsync();
        await WaitUntilAsync(() => source.Status.State != McpServerState.Connected);

        Result<McpTaskSnapshot> result = await source.GetTaskAsync("a", Ct);

        Assert.Equal(ErrorType.Unavailable, result.Error.Type);
    }

    [Fact]
    public async Task CancelTask_SendsTasksCancel()
    {
        McpToolSource source = await StartAsync(FakeMcpServerCatalog.Server("reports"));
        _server.ServerTasks["a"] = new() { ["taskId"] = "a", ["status"] = "working" };

        Result cancelled = await source.CancelTaskAsync("a", Ct);

        Assert.True(cancelled.IsSuccess);
        Assert.Equal(["a"], _server.CancelledTasks);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (McpToolSource source in _sources)
            await source.DisposeAsync();

        await _server.DisposeAsync();
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (int i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, Ct);

        Assert.True(condition());
    }

    private static async Task<Result<ToolOutput>> InvokeAsync(McpToolSource source, MarshalExecutionContext context)
    {
        ITool tool = Assert.Single(await source.GetToolsAsync(Ct));
        return await tool.InvokeAsync(new ToolInvocation(JsonDocument.Parse("""{"quarter":3}""").RootElement.Clone(), context), Ct);
    }

    private async Task<McpToolSource> StartAsync(McpServerDefinition server, bool withTracker = true)
    {
        var source = new McpToolSource(
            server, _catalog, new Transports(_server), NullLoggerFactory.Instance,
            new McpReconnectPolicy(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50)), TimeProvider.System,
            tasks: withTracker ? () => _tracker : () => null);
        _sources.Add(source);
        await source.StartAsync(Ct);
        return source;
    }

    private sealed class RecordingTracker : IMcpTaskTracker
    {
        public List<(McpTaskStart Start, MarshalExecutionContext Context)> Started { get; } = [];

        public Error? Refusal { get; set; }

        public Task<Result<TaskId>> TrackAsync(McpTaskStart start, MarshalExecutionContext context, CancellationToken cancellationToken)
        {
            if (Refusal is { } refusal)
                return Task.FromResult<Result<TaskId>>(refusal);

            Started.Add((start, context));
            return Task.FromResult(Result.Success(TaskId.New()));
        }
    }

    private sealed class Transports(FakeMcpServer server) : IMcpTransportFactory
    {
        public IClientTransport Create(McpConnectionSettings settings) => server.CreateTransport();
    }
}
