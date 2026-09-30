using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Data.Tokens;
using Marshal.Tasks.Executors;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Executors;

public sealed class RunExecutorToolTests : IAsyncLifetime, IDisposable
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 30, 10, 0, 0, TimeSpan.Zero));
    private readonly TaskSignals _signals = new();
    private readonly StubExecutor _executor = new();
    private readonly StubKillSwitch _killSwitch = new();
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
    public async Task Descriptor_ConfirmTierAndPersists_VisibleOnlyWithExecutorsAndWithoutKillSwitch()
    {
        RunExecutorTool tool = Tool();

        Assert.Equal(("tasks.run_executor", RiskTier.Confirm, true), (tool.Descriptor.Id.ToString(), tool.Descriptor.DefaultTier, tool.Descriptor.Persists));
        Assert.Contains("stub: Does stub work.", tool.Descriptor.Description, StringComparison.Ordinal);
        Assert.True(await tool.IsAvailableAsync(Text(OwnerA), Ct));
        Assert.False(await Tool(withExecutor: false).IsAvailableAsync(Text(OwnerA), Ct));
        _killSwitch.Active = true;
        Assert.False(await tool.IsAvailableAsync(Text(OwnerA), Ct));
    }

    [Fact]
    public void Validate_UnknownArgumentOrExecutorOrBadSpec_Denied()
    {
        RunExecutorTool tool = Tool();

        Assert.True(tool.Validate(Arguments(new { executor = "stub", spec = new { }, onDoneGoal = "Report." })).IsSuccess);
        Assert.Equal("tasks.invalid", tool.Validate(Arguments(new { executor = "stub", spec = new { }, onDoneGoal = "Report.", owner = "b" })).Error.Code);
        Assert.Equal("executor.unknown", tool.Validate(Arguments(new { executor = "shell", spec = new { }, onDoneGoal = "Report." })).Error.Code);
        _executor.Valid = false;
        Assert.Equal("executor.invalid_spec", tool.Validate(Arguments(new { executor = "stub", spec = new { }, onDoneGoal = "Report." })).Error.Code);
    }

    [Fact]
    public async Task Invoke_VoiceTurn_OnDoneTaskDeliversToTheSatellite()
    {
        Result<ToolOutput> output = await Tool().InvokeAsync(new ToolInvocation(Arguments(new { executor = "stub", spec = new { repository = "demo" }, onDoneGoal = "Tell me it is ready for review." }), Voice(OwnerA)), Ct);

        TaskRecord task = (await Tasks().GetAsync(TaskId.Parse(output.Value.Reference!, null), Ct)).Value;
        Assert.Equal(("Tell me it is ready for review.", AgentTaskStatus.Waiting, "assist_satellite.kitchen"), (task.Goal, task.Status, task.OriginSatelliteId));
        TaskDelivery delivery = Assert.Single(task.Deliveries);
        Assert.Equal((DeliveryChannel.Voice, "assist_satellite.kitchen"), (delivery.Channel, delivery.Target));
        Assert.Single(_executor.Starts);
    }

    [Fact]
    public async Task Invoke_InsideATaskRun_OnDoneTaskIsItsChild()
    {
        TaskRecord parent = (await Tasks().CreateAsync(new NewTask { Goal = "Keep the demo app tidy.", Trigger = TaskTrigger.Now() }, Ct)).Value;

        Result<ToolOutput> output = await Tool().InvokeAsync(new ToolInvocation(Arguments(new { executor = "stub", spec = new { }, onDoneGoal = "Report." }), Text(OwnerA) with { TaskId = parent.Id, IsInteractive = false }), Ct);

        TaskRecord task = (await Tasks().GetAsync(TaskId.Parse(output.Value.Reference!, null), Ct)).Value;
        Assert.Equal(((TaskId?)parent.Id, TaskCreator.Task, 1), (task.ParentId, task.CreatedBy, task.Depth));
        Assert.Equal(DeliveryChannel.Inbox, Assert.Single(task.Deliveries).Channel);
    }

    private static JsonElement Arguments(object value) => JsonSerializer.SerializeToElement(value);

    private EfTaskStore Tasks() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private RunExecutorTool Tool(bool withExecutor = true) => new(
        new ExecutorService(
            withExecutor ? [_executor] : [],
            new ExecutorCallbackTokens(_database.Factory, _database.Accessor, _time),
            Tasks(),
            _signals,
            new FakeEventPublisher(),
            _database.Factory,
            _database.Accessor,
            Options.Create(new ExecutorOptions { CallbackBaseUrl = new Uri("http://marshal.test/") }),
            _time,
            NullLogger<ExecutorService>.Instance),
        Tasks(),
        _killSwitch,
        Options.Create(new TaskOptions()));

    private sealed class StubExecutor : IExecutor
    {
        public List<ExecutorStart> Starts { get; } = [];

        public bool Valid { get; set; } = true;

        public string Name => "stub";

        public string Description => "Does stub work.";

        public Result ValidateSpec(JsonElement spec) =>
            Valid ? Result.Success() : new Error("executor.invalid_spec", "Bad spec.", ErrorType.Validation);

        public Task<Result<ExecutorStarted>> StartAsync(ExecutorStart start, CancellationToken cancellationToken)
        {
            Starts.Add(start);
            return Task.FromResult(Result.Success(new ExecutorStarted("h", "demo")));
        }

        public Task<Result> CancelAsync(string handle, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    private sealed class StubKillSwitch : IKillSwitch
    {
        public bool Active { get; set; }

        public Task<bool> IsActiveAsync(CancellationToken cancellationToken) => Task.FromResult(Active);

        public Task<int> ActivateAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task ReleaseAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
