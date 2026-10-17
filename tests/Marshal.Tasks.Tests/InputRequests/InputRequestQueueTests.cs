using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.InputRequests;
using Marshal.Abstractions.Mcp;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Core.Mcp;
using Marshal.Core.Tools;
using Marshal.Data;
using Marshal.Data.Conversations;
using Marshal.Data.InputRequests;
using Marshal.Tasks.InputRequests;
using Marshal.Tasks.Running;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.InputRequests;

public sealed class InputRequestQueueTests : IAsyncLifetime, IDisposable
{
    private const string Question = "Which correspondent is this letter from?";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeNotificationSender _sender = new();
    private readonly TaskSignals _signals = new();
    private readonly ActiveTaskRuns _runs = new();
    private readonly FakeMcpServer _server = new();
    private readonly RecordingTaskClient _taskClient = new();
    private McpToolSource _source = null!;
    private ToolRegistry _registry = null!;
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
        _server.WithTool("upload");
        _server.RequestHandler = AskForCorrespondent;
        await StartAsync(untrusted: false);
    }

    public async ValueTask DisposeAsync()
    {
        await _source.DisposeAsync();
        await _server.DisposeAsync();
        await _database.DisposeAsync();
    }

    public void Dispose() => _signals.Dispose();

    [Fact]
    public async Task BackgroundRun_FrozenWithTtl_PushWithLinkNoActions_RunWaits()
    {
        TaskRecord task = await RunningTaskAsync();
        MarshalExecutionContext context = Background(task.Id);

        using ActiveTaskRuns.Scope run = _runs.Begin(task.Id);
        Result<ToolOutput> output = await CallAsync(context);

        Assert.Equal(McpToolSource.InputRequestedText, output.Value.Content);
        InputRequestRecord request = Assert.Single(await Queue().ListPendingAsync(Ct));
        Assert.Equal((InputRequestMode.Form, Question, (TaskId?)task.Id, _time.GetUtcNow() + TimeSpan.FromMinutes(10)), (request.Mode, request.Message, request.TaskId, request.ExpiresAt));
        Assert.Equal(new ToolId("paperless", "upload"), request.Tool);
        Assert.Contains("letter.pdf", request.ArgumentsJson, StringComparison.Ordinal);
        Assert.True(run.State.WaitRequested);
        Assert.Null(run.State.WakeAt);
        NotificationRequest push = Assert.Single(_sender.Sent);
        Assert.Equal(((DeliveryChannel?)DeliveryChannel.Push, NotificationPriority.Normal, $"inbox/inputs/{request.Id}"), (push.Channel, push.Priority, push.Link));
        Assert.Empty(push.Actions);
        Assert.DoesNotContain("correspondent", push.Text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InteractiveTurn_TaskWaitsForTheOutcomeInTheConversation()
    {
        SessionId session = await SessionAsync();

        await CallAsync(Text(OwnerA) with { SessionId = session, IsInteractive = true });

        InputRequestRecord request = Assert.Single(await Queue().ListPendingAsync(Ct));
        TaskRecord task = await GetAsync(request.TaskId!.Value);
        Assert.Equal((AgentTaskStatus.Waiting, (DateTimeOffset?)null, (SessionId?)session), (task.Status, task.NextRunAt, task.OriginSessionId));
        Assert.Equal(DeliveryChannel.Inbox, Assert.Single(task.Deliveries).Channel);
    }

    [Fact]
    public async Task VoiceTurn_TaskDeliversToTheSatellite()
    {
        await CallAsync(Voice(OwnerA));

        InputRequestRecord request = Assert.Single(await Queue().ListPendingAsync(Ct));
        TaskDelivery delivery = Assert.Single((await GetAsync(request.TaskId!.Value)).Deliveries);
        Assert.Equal((DeliveryChannel.Voice, "assist_satellite.kitchen"), (delivery.Channel, delivery.Target));
    }

    [Fact]
    public async Task Answer_RepeatsTheCallOnceWithTheAnswer_ResumesWithTheResult()
    {
        TaskRecord task = await RunningTaskAsync();
        InputRequestRecord request = await AskAsync(task);

        Result<InputRequestRecord> answered = await Queue().AnswerAsync(request.Id, Json("""{"correspondent":"ACME"}"""), Ct);
        Result<InputRequestRecord> again = await Queue().AnswerAsync(request.Id, Json("""{"correspondent":"ACME"}"""), Ct);

        Assert.Equal(InputRequestStatus.Answered, answered.Value.Status);
        Assert.Equal("input_request.decided", again.Error.Code);
        Assert.Equal(3, _server.CallRequests.Count(call => call.Name == "upload"));
        CallToolRequestParams retry = _server.CallRequests.Last();
        Assert.Equal("letter.pdf", retry.Arguments!["file"].GetString());
        Assert.Equal("state-1", retry.RequestState);
        TaskRecord resumed = await GetAsync(task.Id);
        Assert.Equal((AgentTaskStatus.Waiting, (DateTimeOffset?)_time.GetUtcNow()), (resumed.Status, resumed.NextRunAt));
        JsonElement input = Payload(resumed).GetProperty("input");
        Assert.Equal(("answered", "Filed under ACME."), (input.GetProperty("decision").GetString(), input.GetProperty("result").GetString()));
        Assert.False(Payload(resumed).TryGetProperty("taint", out _));
    }

    [Fact]
    public async Task Answer_UntrustedServer_PayloadTainted()
    {
        await _source.DisposeAsync();
        await StartAsync(untrusted: true);
        TaskRecord task = await RunningTaskAsync();
        InputRequestRecord request = await AskAsync(task);

        await Queue().AnswerAsync(request.Id, Json("""{"correspondent":"ACME"}"""), Ct);

        Assert.Equal("mcp:paperless", Payload(await GetAsync(task.Id)).GetProperty("taint").GetString());
    }

    [Theory]
    [InlineData("""{"correspondent":5}""")]
    [InlineData("""{}""")]
    [InlineData("""["ACME"]""")]
    public async Task Answer_NotFittingTheSchema_RefusedAndNothingSent(string answer)
    {
        TaskRecord task = await RunningTaskAsync();
        InputRequestRecord request = await AskAsync(task);
        int calls = _server.CallRequests.Count;

        Result<InputRequestRecord> answered = await Queue().AnswerAsync(request.Id, Json(answer), Ct);

        Assert.Equal(("input_request.invalid_answer", ErrorType.Validation), (answered.Error.Code, answered.Error.Type));
        Assert.Equal(calls, _server.CallRequests.Count);
        Assert.Equal(InputRequestStatus.Pending, (await Queue().GetAsync(request.Id, Ct))!.Status);
    }

    [Fact]
    public async Task Answer_UrlRequestDone_RepeatsWithoutContent()
    {
        _server.RequestHandler = parameters => parameters.InputResponses is { Count: > 0 }
            ? new CallToolResult { Content = [new TextContentBlock { Text = "Logged in." }] }
            : throw new InputRequiredException(new Dictionary<string, InputRequest>
            {
                ["login"] = InputRequest.ForElicitation(new ElicitRequestParams { Mode = "url", Message = "Log in.", Url = "https://paperless.example/login", ElicitationId = "e1" }),
            }, null);
        TaskRecord task = await RunningTaskAsync();
        InputRequestRecord request = await AskAsync(task);

        Result<InputRequestRecord> withContent = await Queue().AnswerAsync(request.Id, Json("""{"password":"x"}"""), Ct);
        Result<InputRequestRecord> done = await Queue().AnswerAsync(request.Id, null, Ct);

        Assert.Equal("input_request.invalid_answer", withContent.Error.Code);
        Assert.True(done.IsSuccess);
        Assert.Equal("Logged in.", Payload(await GetAsync(task.Id)).GetProperty("input").GetProperty("result").GetString());
    }

    [Fact]
    public async Task Answer_ServerAsksAgain_ChainedRequestAndTaskKeepsWaiting()
    {
        _server.RequestHandler = parameters => parameters.InputResponses?.Count switch
        {
            null => throw Ask("a", Question, "correspondent", "state-1"),
            1 when parameters.RequestState == "state-1" => throw Ask("b", "Which tag?", "tag", "state-2"),
            _ => new CallToolResult { Content = [new TextContentBlock { Text = "Filed." }] },
        };
        TaskRecord task = await RunningTaskAsync();
        InputRequestRecord first = await AskAsync(task);

        await Queue().AnswerAsync(first.Id, Json("""{"correspondent":"ACME"}"""), Ct);

        InputRequestRecord second = Assert.Single(await Queue().ListPendingAsync(Ct));
        Assert.Equal(("Which tag?", (TaskId?)task.Id), (second.Message, second.TaskId));
        Assert.Null((await GetAsync(task.Id)).TriggerPayload);

        await Queue().AnswerAsync(second.Id, Json("""{"tag":"bills"}"""), Ct);

        Assert.Equal("Filed.", Payload(await GetAsync(task.Id)).GetProperty("input").GetProperty("result").GetString());
    }

    [Fact]
    public async Task Decline_NothingSent_TaskResumesWithTheRefusal()
    {
        TaskRecord task = await RunningTaskAsync();
        InputRequestRecord request = await AskAsync(task);
        int calls = _server.CallRequests.Count;

        Result<InputRequestRecord> declined = await Queue().DeclineAsync(request.Id, Ct);

        Assert.Equal(InputRequestStatus.Declined, declined.Value.Status);
        Assert.Equal(calls, _server.CallRequests.Count);
        Assert.Equal("declined", Payload(await GetAsync(task.Id)).GetProperty("input").GetProperty("decision").GetString());
    }

    [Fact]
    public async Task Expiry_NothingSent_TaskResumesRejectedExpired()
    {
        TaskRecord task = await RunningTaskAsync();
        InputRequestRecord request = await AskAsync(task);
        int calls = _server.CallRequests.Count;
        _time.Advance(TimeSpan.FromMinutes(11));

        await Queue().ExpireDueAsync(Ct);

        Assert.Equal(InputRequestStatus.Expired, (await Queue().GetAsync(request.Id, Ct))!.Status);
        Assert.Equal(calls, _server.CallRequests.Count);
        JsonElement input = Payload(await GetAsync(task.Id)).GetProperty("input");
        Assert.Equal(("rejected", "expired"), (input.GetProperty("decision").GetString(), input.GetProperty("reason").GetString()));
        Assert.Equal("input_request.decided", (await Queue().AnswerAsync(request.Id, Json("""{"correspondent":"ACME"}"""), Ct)).Error.Code);
    }

    [Fact]
    public async Task Answer_AfterExpiry_Expired()
    {
        TaskRecord task = await RunningTaskAsync();
        InputRequestRecord request = await AskAsync(task);
        _time.Advance(TimeSpan.FromMinutes(11));

        Result<InputRequestRecord> answered = await Queue().AnswerAsync(request.Id, Json("""{"correspondent":"ACME"}"""), Ct);

        Assert.Equal("input_request.expired", answered.Error.Code);
    }

    [Fact]
    public async Task OtherOwner_CannotSeeOrAnswer()
    {
        TaskRecord task = await RunningTaskAsync();
        InputRequestRecord request = await AskAsync(task);

        using (_database.Accessor.Use(Text(OwnerB)))
        {
            Assert.Empty(await Queue().ListPendingAsync(Ct));
            Assert.Equal("input_request.not_found", (await Queue().AnswerAsync(request.Id, Json("""{"correspondent":"ACME"}"""), Ct)).Error.Code);
        }
    }

    [Fact]
    public async Task ServerTaskInput_AnswerSentWithTasksUpdate_NoRepeatedCall_TaskKeepsWaiting()
    {
        (TaskRecord task, InputRequestRecord request) = await ServerTaskInputAsync();

        Result<InputRequestRecord> answered = await Queue().AnswerAsync(request.Id, Json("""{"correspondent":"ACME"}"""), Ct);

        Assert.Equal(InputRequestStatus.Answered, answered.Value.Status);
        (string Server, string Task, string Key, bool Accept, JsonElement? Content) update = Assert.Single(_taskClient.Answers);
        Assert.Equal(("paperless", "task-7", "q1", true, "ACME"), (update.Server, update.Task, update.Key, update.Accept, update.Content!.Value.GetProperty("correspondent").GetString()));
        Assert.Empty(_server.CallRequests);
        TaskRecord after = await GetAsync(task.Id);
        Assert.Equal((AgentTaskStatus.Waiting, (string?)null), (after.Status, after.TriggerPayload));
    }

    [Fact]
    public async Task ServerTaskInput_DeclineAndExpiry_DeclinedOnTheServer_TaskKeepsWaiting()
    {
        (TaskRecord task, InputRequestRecord declined) = await ServerTaskInputAsync("q1");
        (_, InputRequestRecord expired) = await ServerTaskInputAsync("q2", task);

        await Queue().DeclineAsync(declined.Id, Ct);
        _time.Advance(TimeSpan.FromMinutes(11));
        await Queue().ExpireDueAsync(Ct);

        Assert.Equal([("q1", false), ("q2", false)], _taskClient.Answers.Select(answer => (answer.Key, answer.Accept)));
        Assert.Equal(InputRequestStatus.Expired, (await Queue().GetAsync(expired.Id, Ct))!.Status);
        Assert.Null((await GetAsync(task.Id)).TriggerPayload);
    }

    private async Task<(TaskRecord Task, InputRequestRecord Request)> ServerTaskInputAsync(string key = "q1", TaskRecord? task = null)
    {
        task ??= (await Tasks().CreateAsync(new NewTask { Goal = "Wait for the export.", Trigger = TaskTrigger.Now(), AwaitsResume = true }, Ct)).Value;
        string schema = """{"type":"object","properties":{"correspondent":{"type":"string"}},"required":["correspondent"]}""";
        InputRequestRecord request = await Queue().CreateAsync(
            new NewInputRequest("paperless", new ToolId("paperless", "upload"), "{}", InputRequestMode.Form, Question, schema, null, ServerTask: new McpServerTaskInput("task-7", key)),
            Background(task.Id),
            Ct);
        return (task, request);
    }

    private static CallToolResult AskForCorrespondent(CallToolRequestParams parameters)
    {
        if (parameters.InputResponses is not { Count: > 0 } responses)
            throw Ask("q1", Question, "correspondent", "state-1");

        ElicitResult? answer = responses.Values.First().Deserialize(InputResponse.ElicitResultJsonTypeInfo);
        return new CallToolResult { Content = [new TextContentBlock { Text = $"Filed under {answer!.Content!["correspondent"].GetString()}." }] };
    }

    private static InputRequiredException Ask(string key, string message, string property, string state) => new(
        new Dictionary<string, InputRequest>
        {
            [key] = InputRequest.ForElicitation(new ElicitRequestParams
            {
                Mode = "form",
                Message = message,
                RequestedSchema = new ElicitRequestParams.RequestSchema
                {
                    Properties = new Dictionary<string, ElicitRequestParams.PrimitiveSchemaDefinition> { [property] = new ElicitRequestParams.StringSchema() },
                    Required = [property],
                },
            }),
        },
        state);

    private async Task StartAsync(bool untrusted)
    {
        var catalog = new FakeMcpServerCatalog();
        _source = new McpToolSource(
            FakeMcpServerCatalog.Server("paperless", untrusted: untrusted), catalog, new Transports(_server), NullLoggerFactory.Instance,
            new McpReconnectPolicy(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(50)), TimeProvider.System, inputs: Queue);
        await _source.StartAsync(Ct);
        _registry = new ToolRegistry([_source]);
        await _registry.RefreshAsync(Ct);
    }

    private async Task<Result<ToolOutput>> CallAsync(MarshalExecutionContext context)
    {
        RegisteredTool tool = _registry.Current.FindById(new ToolId("paperless", "upload"))!;
        using (_database.Accessor.Use(context))
            return await tool.Tool.InvokeAsync(new ToolInvocation(Json("""{"file":"letter.pdf"}""")!.Value, context), Ct);
    }

    private async Task<InputRequestRecord> AskAsync(TaskRecord task)
    {
        using (_runs.Begin(task.Id))
            await CallAsync(Background(task.Id));

        // The run ended as waiting.
        await Tasks().UpdateAsync((await GetAsync(task.Id)) with { Status = AgentTaskStatus.Waiting, NextRunAt = null }, Ct);
        return Assert.Single(await Queue().ListPendingAsync(Ct));
    }

    private static MarshalExecutionContext Background(TaskId task) =>
        Text(OwnerA) with { Source = SessionSource.Task, IsInteractive = false, TaskId = task };

    private async Task<TaskRecord> RunningTaskAsync()
    {
        TaskRecord task = (await Tasks().CreateAsync(new NewTask { Goal = "File the scanned letter.", Trigger = TaskTrigger.Now() }, Ct)).Value;
        return (await Tasks().UpdateAsync(task with { Status = AgentTaskStatus.Running }, Ct)).Value;
    }

    private async Task<SessionId> SessionAsync()
    {
        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        var id = SessionId.New();
        context.Sessions.Add(new Session
        {
            Id = id,
            OwnerId = OwnerA,
            Source = SessionSource.Web,
            IdentityEvidence = "{}",
            ResolvedBy = "cookie",
            CreatedAt = _time.GetUtcNow(),
            LastTurnAt = _time.GetUtcNow(),
        });
        await context.SaveChangesAsync(Ct);
        return id;
    }

    private static JsonElement? Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement Payload(TaskRecord task) => JsonDocument.Parse(task.TriggerPayload!).RootElement.Clone();

    private async Task<TaskRecord> GetAsync(TaskId id) => (await Tasks().GetAsync(id, Ct)).Value;

    private EfTaskStore Tasks() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private InputRequestQueue Queue() => new(
        new EfInputRequestStore(_database.Factory, _time),
        Tasks(),
        _sender,
        _registry,
        new DefaultTierRiskEngine(),
        _database.Accessor,
        _database.Factory,
        _signals,
        _runs,
        _taskClient,
        Options.Create(new TaskOptions()),
        _time,
        NullLogger<InputRequestQueue>.Instance);

    private sealed class RecordingTaskClient : IMcpTaskClient
    {
        public List<(string Server, string Task, string Key, bool Accept, JsonElement? Content)> Answers { get; } = [];

        public Task<Result<McpTaskSnapshot>> GetTaskAsync(string serverId, string taskId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Abstractions.Result> AnswerInputAsync(string serverId, string taskId, string inputKey, bool accept, JsonElement? content, CancellationToken cancellationToken)
        {
            Answers.Add((serverId, taskId, inputKey, accept, content?.Clone()));
            return Task.FromResult(Abstractions.Result.Success());
        }

        public Task<Abstractions.Result> CancelTaskAsync(string serverId, string taskId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class DefaultTierRiskEngine : IRiskEngine
    {
        public Task<RiskAssessment> AssessAsync(RegisteredTool tool, JsonElement arguments, CancellationToken cancellationToken) =>
            Task.FromResult(new RiskAssessment(tool.Descriptor.DefaultTier, []));

        public RiskProfile GetProfile(RegisteredTool tool) => throw new NotSupportedException();
    }

    private sealed class Transports(FakeMcpServer server) : IMcpTransportFactory
    {
        public IClientTransport Create(McpConnectionSettings settings) => server.CreateTransport();
    }
}
