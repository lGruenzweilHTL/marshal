using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tokens;
using Marshal.Data;
using Marshal.Data.Tasks;
using Marshal.Data.Tokens;
using Marshal.Tasks.Executors;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Executors;

public sealed class ExecutorServiceTests : IAsyncLifetime, IDisposable
{
    private static readonly JsonElement _spec = JsonSerializer.SerializeToElement(new { repository = "demo", prompt = "Add a feature file." });

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));
    private readonly TaskSignals _signals = new();
    private readonly FakeExecutor _executor = new();
    private readonly RecordingPublisher _events = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    public void Dispose() => _signals.Dispose();

    [Fact]
    public async Task Start_WaitingTaskSessionAndCallbackToken_ExecutorGetsTheCallback()
    {
        StartedSession started = (await Service().StartAsync("fake", _spec, OnDone(), Ct)).Value;

        ExecutorStart start = Assert.Single(_executor.Starts);
        Assert.Equal(started.SessionId, start.SessionId);
        Assert.Equal(new Uri($"http://marshal.test/base/api/v1/executor/callbacks/{started.SessionId}"), start.CallbackUrl);
        Assert.Equal("demo", start.Spec.GetProperty("repository").GetString());

        TaskRecord task = (await Tasks().GetAsync(started.TaskId, Ct)).Value;
        Assert.Equal((AgentTaskStatus.Waiting, (DateTimeOffset?)null, "Tell me what changed."), (task.Status, task.NextRunAt, task.Goal));

        ExecutorSession session = await SessionAsync(started.SessionId);
        Assert.Equal((started.TaskId, "fake", "handle-1", "demo", ExecutorSessionStatus.Running), (session.TaskId, session.Executor, session.Handle, session.Target, session.Status));

        ApiTokenInfo token = (await TokenService().ValidateAsync(start.CallbackToken, Ct)).Value;
        Assert.Equal((ApiTokenKind.ExecutorCallback, (TaskId?)started.TaskId, (ApiTokenId?)token.Id), (token.Kind, token.TaskId, session.CallbackTokenId));
    }

    [Fact]
    public async Task Start_UnknownExecutorOrInvalidSpec_NothingCreated()
    {
        Result<StartedSession> unknown = await Service().StartAsync("other", _spec, OnDone(), Ct);
        _executor.SpecError = new Error("executor.invalid_spec", "Unknown repository.", ErrorType.Validation);
        Result<StartedSession> invalid = await Service().StartAsync("fake", _spec, OnDone(), Ct);

        Assert.Equal(("executor.unknown", "executor.invalid_spec"), (unknown.Error.Code, invalid.Error.Code));
        Assert.Empty(_executor.Starts);
        Assert.Empty(await Tasks().ListAsync(new TaskFilter(), Ct));
    }

    [Fact]
    public async Task Start_ExecutorFails_TokenRevokedSessionAndTaskFailed()
    {
        _executor.StartError = new Error("executor.start_failed", "The executor host is unreachable.", ErrorType.Unavailable);

        Result<StartedSession> result = await Service().StartAsync("fake", _spec, OnDone(), Ct);

        Assert.Equal("executor.start_failed", result.Error.Code);
        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        ExecutorSession session = await context.ExecutorSessions.SingleAsync(Ct);
        Assert.Equal(ExecutorSessionStatus.Failed, session.Status);
        Assert.Equal("token.invalid", (await TokenService().ValidateAsync(Assert.Single(_executor.Starts).CallbackToken, Ct)).Error.Code);
        TaskRecord task = (await Tasks().GetAsync(session.TaskId, Ct)).Value;
        Assert.Equal((AgentTaskStatus.Failed, "executor_start_failed"), (task.Status, task.StatusReason));
    }

    [Fact]
    public async Task Callback_TokenOfAnotherSession_Forbidden()
    {
        StartedSession first = (await Service().StartAsync("fake", _spec, OnDone(), Ct)).Value;
        StartedSession second = (await Service().StartAsync("fake", _spec, OnDone(), Ct)).Value;
        ApiTokenId firstToken = (await SessionAsync(first.SessionId)).CallbackTokenId!.Value;

        Result result = await Service().HandleAsync(second.SessionId, firstToken, new ExecutorReport("completed", Branch: "marshal/x", Diff: "+x"), Ct);

        Assert.Equal(("executor.wrong_session", ErrorType.Forbidden), (result.Error.Code, result.Error.Type));
        Assert.Equal(ExecutorSessionStatus.Running, (await SessionAsync(second.SessionId)).Status);
        Assert.Empty(_events.Published);
    }

    [Fact]
    public async Task Callback_OtherOwner_Forbidden()
    {
        StartedSession started = (await Service().StartAsync("fake", _spec, OnDone(), Ct)).Value;
        ApiTokenId token = (await SessionAsync(started.SessionId)).CallbackTokenId!.Value;
        _database.Accessor.Current = Text(OwnerB);

        Result result = await Service().HandleAsync(started.SessionId, token, new ExecutorReport("failed"), Ct);

        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
    }

    [Fact]
    public async Task Progress_SessionKeepsRunning()
    {
        StartedSession started = (await Service().StartAsync("fake", _spec, OnDone(), Ct)).Value;
        ApiTokenId token = (await SessionAsync(started.SessionId)).CallbackTokenId!.Value;

        Result result = await Service().HandleAsync(started.SessionId, token, new ExecutorReport("progress", "Added feature.txt."), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(ExecutorSessionStatus.Running, (await SessionAsync(started.SessionId)).Status);
        Assert.Equal(AgentTaskStatus.Waiting, (await Tasks().GetAsync(started.TaskId, Ct)).Value.Status);
    }

    [Fact]
    public async Task Completed_ProposalEventAndResume_TokenRevoked()
    {
        StartedSession started = (await Service().StartAsync("fake", _spec, OnDone(), Ct)).Value;
        ExecutorSession session = await SessionAsync(started.SessionId);
        string plaintext = Assert.Single(_executor.Starts).CallbackToken;
        _time.Advance(TimeSpan.FromMinutes(20));

        Result result = await Service().HandleAsync(started.SessionId, session.CallbackTokenId!.Value,
            new ExecutorReport("completed", Branch: $"marshal/{session.Handle}", Diff: "+change", Summary: "Added feature.txt with a note.\n\nDetails follow.", CostUsd: 0.12m), Ct);

        Assert.True(result.IsSuccess);
        ExecutorSession finished = await SessionAsync(started.SessionId);
        Assert.Equal((ExecutorSessionStatus.Done, (DateTimeOffset?)_time.GetUtcNow()), (finished.Status, finished.FinishedAt));
        Assert.Equal("token.invalid", (await TokenService().ValidateAsync(plaintext, Ct)).Error.Code);

        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        Proposal proposal = await context.Proposals.SingleAsync(Ct);
        Assert.Equal((ProposalKind.Code, "demo", "marshal/handle-1", "Added feature.txt with a note.", "+change", ProposalStatus.Open, (TaskId?)started.TaskId),
            (proposal.Kind, proposal.Target, proposal.Branch, proposal.Title, proposal.Diff, proposal.Status, proposal.TaskId));

        MarshalEvent published = Assert.Single(_events.Published);
        Assert.Equal(("executor", "completed", started.SessionId.ToString(), (Abstractions.Identity.OwnerId?)OwnerA), (published.Source, published.Type, published.Entity, published.OwnerId));
        Assert.Equal(proposal.Id.ToString(), published.Payload.GetProperty("proposal").GetString());

        TaskRecord task = (await Tasks().GetAsync(started.TaskId, Ct)).Value;
        Assert.Equal(_time.GetUtcNow(), task.NextRunAt);
        JsonElement payload = JsonDocument.Parse(task.TriggerPayload!).RootElement.GetProperty(ExecutorService.PayloadKey);
        Assert.Equal(("completed", "demo", proposal.Id.ToString()), (payload.GetProperty("status").GetString(), payload.GetProperty("target").GetString(), payload.GetProperty("proposal").GetString()));
        Assert.Contains("web UI", payload.GetProperty("review").GetString(), StringComparison.Ordinal);
        Assert.True(_signals.WaitAsync(Ct).IsCompleted);
    }

    [Fact]
    public async Task Failed_NoProposal_ResumedWithTheFailure()
    {
        StartedSession started = (await Service().StartAsync("fake", _spec, OnDone(), Ct)).Value;
        ApiTokenId token = (await SessionAsync(started.SessionId)).CallbackTokenId!.Value;

        await Service().HandleAsync(started.SessionId, token, new ExecutorReport("failed", "Claude Code exited with code 1."), Ct);

        Assert.Equal(ExecutorSessionStatus.Failed, (await SessionAsync(started.SessionId)).Status);
        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        Assert.Empty(await context.Proposals.ToListAsync(Ct));
        Assert.Equal("failed", Assert.Single(_events.Published).Type);
        JsonElement payload = JsonDocument.Parse((await Tasks().GetAsync(started.TaskId, Ct)).Value.TriggerPayload!).RootElement.GetProperty(ExecutorService.PayloadKey);
        Assert.Equal(("failed", "Claude Code exited with code 1."), (payload.GetProperty("status").GetString(), payload.GetProperty("message").GetString()));
        Assert.False(payload.TryGetProperty("proposal", out _));
    }

    [Fact]
    public async Task Callback_AfterTheSessionEnded_Conflict()
    {
        StartedSession started = (await Service().StartAsync("fake", _spec, OnDone(), Ct)).Value;
        ApiTokenId token = (await SessionAsync(started.SessionId)).CallbackTokenId!.Value;
        await Service().HandleAsync(started.SessionId, token, new ExecutorReport("cancelled"), Ct);

        Result again = await Service().HandleAsync(started.SessionId, token, new ExecutorReport("completed", Branch: "b", Diff: "d"), Ct);

        Assert.Equal(ErrorType.Conflict, again.Error.Type);
        Assert.Single(_events.Published);
    }

    private static NewTask OnDone() => new() { Goal = "Tell me what changed.", Trigger = TaskTrigger.Now() };

    private async Task<ExecutorSession> SessionAsync(Guid id)
    {
        await using MarshalDbContext context = _database.CreateContext(OwnerA);
        return await context.ExecutorSessions.SingleAsync(row => row.Id == id, Ct);
    }

    private EfTaskStore Tasks() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private ApiTokenService TokenService() => new(_database.Factory, _database.Accessor, _time);

    private ExecutorService Service() => new(
        [_executor],
        new ExecutorCallbackTokens(_database.Factory, _database.Accessor, _time),
        Tasks(),
        _signals,
        _events,
        _database.Factory,
        _database.Accessor,
        Options.Create(new ExecutorOptions { CallbackBaseUrl = new Uri("http://marshal.test/base/") }),
        _time,
        NullLogger<ExecutorService>.Instance);

    private sealed class FakeExecutor : IExecutor
    {
        public List<ExecutorStart> Starts { get; } = [];

        public Error? SpecError { get; set; }

        public Error? StartError { get; set; }

        public string Name => "fake";

        public string Description => "A fake executor.";

        public Result ValidateSpec(JsonElement spec) => SpecError is { } error ? error : Result.Success();

        public Task<Result<ExecutorStarted>> StartAsync(ExecutorStart start, CancellationToken cancellationToken)
        {
            Starts.Add(start);
            return Task.FromResult<Result<ExecutorStarted>>(StartError is { } error ? error : new ExecutorStarted($"handle-{Starts.Count}", "demo"));
        }

        public Task<Result> CancelAsync(string handle, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result> MergeAsync(string handle, string target, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result> DiscardAsync(string handle, string target, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    private sealed class RecordingPublisher : IEventPublisher
    {
        public List<MarshalEvent> Published { get; } = [];

        public void Publish(MarshalEvent @event) => Published.Add(@event);
    }
}
