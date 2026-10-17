using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Tools;
using Marshal.Core.Mcp;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Marshal.Core.Tests.Mcp;

public sealed class McpElicitationTests : IAsyncDisposable
{
    private const string Secret = "Which correspondent is this letter from? (see https://paperless.example/login?state=abc)";

    private readonly FakeMcpServer _server = new();
    private readonly FakeMcpServerCatalog _catalog = new();
    private readonly RecordingInputs _inputs = new();
    private readonly List<McpToolSource> _sources = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> Revisions => ["2026-07-28", "2025-11-25", "2025-06-18"];

    [Theory]
    [MemberData(nameof(Revisions))]
    public async Task FormRequest_FrozenAsPendingInputWithoutContentInToolResult(string revision)
    {
        _server.ProtocolVersion = revision;
        _server.WithTool("upload");
        _server.RequestHandler = AskOnce(Form(), "done:ACME");
        ITool tool = await ToolAsync();
        MarshalExecutionContext context = TestExecutionContext.Text();

        Result<ToolOutput> result = await tool.InvokeAsync(new ToolInvocation(Arguments("""{"file":"letter.pdf"}"""), context), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(McpToolSource.InputRequestedText, result.Value.Content);
        Assert.DoesNotContain("correspondent", result.Value.Content, StringComparison.OrdinalIgnoreCase);
        (NewInputRequest request, MarshalExecutionContext asked) = Assert.Single(_inputs.Created);
        Assert.Equal(("paperless", new ToolId("paperless", "upload"), InputRequestMode.Form), (request.ServerId, request.Tool, request.Mode));
        Assert.Equal("letter.pdf", JsonDocument.Parse(request.ArgumentsJson).RootElement.GetProperty("file").GetString());
        Assert.Equal(Secret, request.Message);
        Assert.Contains("\"correspondent\"", request.RequestedSchemaJson, StringComparison.Ordinal);
        Assert.Same(context, asked);
        Assert.Equal([_inputs.LastId], context.Control.InputRequests);
    }

    [Theory]
    [MemberData(nameof(Revisions))]
    public async Task UrlRequest_UrlOnlyInTheRequest(string revision)
    {
        _server.ProtocolVersion = revision;
        _server.WithTool("upload");
        _server.RequestHandler = AskOnce(new ElicitRequestParams { Mode = "url", Message = "Log in to Paperless.", Url = "https://paperless.example/login?state=abc", ElicitationId = "e1" }, "done");
        ITool tool = await ToolAsync();

        Result<ToolOutput> result = await tool.InvokeAsync(new ToolInvocation(Arguments("{}"), TestExecutionContext.Voice()), Ct);

        Assert.DoesNotContain("paperless.example", result.Value.Content, StringComparison.Ordinal);
        NewInputRequest request = Assert.Single(_inputs.Created).Request;
        Assert.Equal((InputRequestMode.Url, "https://paperless.example/login?state=abc", null), (request.Mode, request.Url, request.RequestedSchemaJson));
    }

    [Theory]
    [MemberData(nameof(Revisions))]
    public async Task Replay_RepeatsOnceWithTheAnswer(string revision)
    {
        _server.ProtocolVersion = revision;
        _server.WithTool("upload");
        _server.RequestHandler = AskOnce(Form(), "done");
        ITool tool = await ToolAsync();
        var replay = new McpInputReplay([new McpInputAnswer(McpInputAnswer.SignatureOf(Form()), InputRequestMode.Form, JsonDocument.Parse("""{"correspondent":"ACME"}""").RootElement.Clone())]);

        Result<ToolOutput> result;
        using (replay.Use())
            result = await tool.InvokeAsync(new ToolInvocation(Arguments("{}"), TestExecutionContext.Text()), Ct);

        Assert.Equal("done:ACME", result.Value.Content);
        Assert.Null(replay.Created);
        Assert.Empty(_inputs.Created);
    }

    [Fact]
    public async Task Replay_RequestStateIsEchoedToTheServer()
    {
        _server.WithTool("upload");
        _server.RequestHandler = AskOnce(Form(), "done");
        ITool tool = await ToolAsync();
        var replay = new McpInputReplay([new McpInputAnswer(McpInputAnswer.SignatureOf(Form()), InputRequestMode.Form, JsonDocument.Parse("""{"correspondent":"ACME"}""").RootElement.Clone())]);

        using (replay.Use())
            await tool.InvokeAsync(new ToolInvocation(Arguments("{}"), TestExecutionContext.Text()), Ct);

        CallToolRequestParams retry = _server.CallRequests.Last();
        Assert.Equal("state-1", retry.RequestState);
        Assert.Single(retry.InputResponses!);
    }

    [Fact]
    public async Task Replay_SecondRequest_IsChainedWithTheFirstAnswer()
    {
        _server.WithTool("upload");
        ElicitRequestParams second = new() { Mode = "form", Message = "Which tag?", RequestedSchema = Schema("tag") };
        _server.RequestHandler = request => request.InputResponses?.Count switch
        {
            null => throw new InputRequiredException(new Dictionary<string, InputRequest> { ["a"] = InputRequest.ForElicitation(Form()) }, "state-1"),
            1 when request.RequestState == "state-1" => throw new InputRequiredException(new Dictionary<string, InputRequest> { ["b"] = InputRequest.ForElicitation(second) }, "state-2"),
            _ => new CallToolResult { Content = [new TextContentBlock { Text = "done" }] },
        };
        ITool tool = await ToolAsync();
        var replay = new McpInputReplay([new McpInputAnswer(McpInputAnswer.SignatureOf(Form()), InputRequestMode.Form, JsonDocument.Parse("""{"correspondent":"ACME"}""").RootElement.Clone())]);

        Result<ToolOutput> result;
        using (replay.Use())
            result = await tool.InvokeAsync(new ToolInvocation(Arguments("{}"), TestExecutionContext.Text()), Ct);

        Assert.Equal(McpToolSource.InputRequestedText, result.Value.Content);
        Assert.NotNull(replay.Created);
        NewInputRequest chained = Assert.Single(_inputs.Created).Request;
        Assert.Equal("Which tag?", chained.Message);
        McpInputAnswer previous = Assert.Single(McpInputAnswer.ParseList(chained.PreviousAnswersJson));
        Assert.Equal("ACME", previous.Content!.Value.GetProperty("correspondent").GetString());
    }

    [Fact]
    public async Task NoInputQueue_FailsWithoutContent()
    {
        _server.WithTool("upload");
        _server.RequestHandler = AskOnce(Form(), "done");
        ITool tool = await ToolAsync(withInputs: false);

        Result<ToolOutput> result = await tool.InvokeAsync(new ToolInvocation(Arguments("{}"), TestExecutionContext.Text()), Ct);

        Assert.Equal("mcp.input_unavailable", result.Error.Code);
        Assert.DoesNotContain("correspondent", result.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InputRequest_KeepsTheConnection()
    {
        _server.WithTool("upload");
        _server.RequestHandler = AskOnce(Form(), "done");
        ITool tool = await ToolAsync();

        await tool.InvokeAsync(new ToolInvocation(Arguments("{}"), TestExecutionContext.Text()), Ct);
        _server.RequestHandler = null;

        Assert.True((await tool.InvokeAsync(new ToolInvocation(Arguments("{}"), TestExecutionContext.Text()), Ct)).IsSuccess);
        Assert.Equal(1, _server.Connections);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (McpToolSource source in _sources)
            await source.DisposeAsync();

        await _server.DisposeAsync();
    }

    private static ElicitRequestParams Form() => new() { Mode = "form", Message = Secret, RequestedSchema = Schema("correspondent") };

    private static ElicitRequestParams.RequestSchema Schema(string property) => new()
    {
        Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { [property] = new ElicitRequestParams.StringSchema() },
        Required = [property],
    };

    // Asks once; on the retry answers "<done>:<first answer's first value>".
    private static Func<CallToolRequestParams, CallToolResult> AskOnce(ElicitRequestParams request, string done) => parameters =>
    {
        if (parameters.InputResponses is not { Count: > 0 } responses)
            throw new InputRequiredException(new Dictionary<string, InputRequest> { ["q1"] = InputRequest.ForElicitation(request) }, "state-1");

        ElicitResult? answer = responses.Values.First().Deserialize(InputResponse.ElicitResultJsonTypeInfo);
        string value = answer?.Content?.Values.FirstOrDefault().GetString() ?? "";
        return new CallToolResult { Content = [new TextContentBlock { Text = done.Contains(':', StringComparison.Ordinal) || value.Length == 0 ? done : $"{done}:{value}" }] };
    };

    private static JsonElement Arguments(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private async Task<ITool> ToolAsync(bool withInputs = true)
    {
        var source = new McpToolSource(
            FakeMcpServerCatalog.Server("paperless"), _catalog, new Transports(_server), NullLoggerFactory.Instance,
            new McpReconnectPolicy(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50)), TimeProvider.System,
            inputs: withInputs ? () => _inputs : () => null);
        _sources.Add(source);
        await source.StartAsync(Ct);
        return Assert.Single(await source.GetToolsAsync(Ct));
    }

    private sealed class RecordingInputs : IInputRequestQueue
    {
        public List<(NewInputRequest Request, MarshalExecutionContext Context)> Created { get; } = [];

        public InputRequestId LastId { get; private set; }

        public Task<InputRequestRecord> CreateAsync(NewInputRequest request, MarshalExecutionContext context, CancellationToken cancellationToken)
        {
            Created.Add((request, context));
            LastId = InputRequestId.New();
            return Task.FromResult(new InputRequestRecord
            {
                Id = LastId,
                OwnerId = context.OwnerId,
                ServerId = request.ServerId,
                Tool = request.Tool,
                ArgumentsJson = request.ArgumentsJson,
                Mode = request.Mode,
                Message = request.Message,
                Nonce = "n",
                Language = context.Language,
                CreatedAt = DateTimeOffset.UnixEpoch,
                ExpiresAt = DateTimeOffset.UnixEpoch,
            });
        }

        public Task<InputRequestRecord?> GetAsync(InputRequestId id, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<InputRequestRecord>> ListPendingAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<InputRequestRecord>> AnswerAsync(InputRequestId id, JsonElement? content, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Result<InputRequestRecord>> DeclineAsync(InputRequestId id, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class Transports(FakeMcpServer server) : IMcpTransportFactory
    {
        public IClientTransport Create(McpConnectionSettings settings) => server.CreateTransport();
    }
}
