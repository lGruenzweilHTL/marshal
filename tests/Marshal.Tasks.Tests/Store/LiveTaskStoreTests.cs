using Marshal.Abstractions;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Store;

public sealed class LiveTaskStoreTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeLiveEventPublisher _live = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task CreateAndUpdate_PublishTaskChangedToTheOwner()
    {
        TaskRecord task = (await Store().CreateAsync(new NewTask { Goal = "Briefing.", Trigger = TaskTrigger.Now(), Name = "briefing" }, Ct)).Value;
        await Store().UpdateAsync(task with { Status = AgentTaskStatus.Running }, Ct);

        LiveEvent[] events = [.. _live.Events];
        Assert.Equal(2, events.Length);
        Assert.All(events, live => Assert.Equal(("tasks", "task.changed", OwnerA), (live.Topic, live.Type, live.Owner)));
        TaskChangedPayload payload = Assert.IsType<TaskChangedPayload>(events[1].Payload);
        Assert.Equal((task.Id, "briefing", AgentTaskStatus.Running), (payload.TaskId, payload.Name, payload.Status));
    }

    [Fact]
    public async Task FailedUpdate_NothingPublished()
    {
        TaskRecord task = (await Store().CreateAsync(new NewTask { Goal = "Briefing.", Trigger = TaskTrigger.Now() }, Ct)).Value;
        _live.Events.Clear();

        Result<TaskRecord> stale = await Store().UpdateAsync(task with { RowVersion = task.RowVersion + 5 }, Ct);

        Assert.True(stale.IsFailure);
        Assert.Empty(_live.Events);
    }

    [Fact]
    public async Task ResultOnHandedOffTask_PublishesConversationResultToTheOwner()
    {
        var session = SessionId.New();
        TaskRecord task = (await Store().CreateAsync(new NewTask { Goal = "Check the printer.", Trigger = TaskTrigger.Now(), OriginSessionId = session }, Ct)).Value;
        task = (await Store().UpdateAsync(task with { Status = AgentTaskStatus.Running }, Ct)).Value;
        _live.Events.Clear();
        DateTimeOffset now = _time.GetUtcNow();

        task = (await Store().UpdateAsync(task with { Status = AgentTaskStatus.Done, Result = new TaskResult("Done at 14:02.", "The printer finished at 14:02."), ResultAt = now }, Ct)).Value;
        await Store().UpdateAsync(task with { StatusReason = "unchanged result" }, Ct);

        LiveEvent result = Assert.Single(_live.Events, live => live.Topic == "conversations");
        Assert.Equal(("conversation.result", OwnerA), (result.Type, result.Owner));
        ConversationResultPayload payload = Assert.IsType<ConversationResultPayload>(result.Payload);
        Assert.Equal((session, task.Id, now, "The printer finished at 14:02."), (payload.SessionId, payload.TaskId, payload.ResultAt, payload.Body));
    }

    [Fact]
    public async Task ResultWithoutOriginSession_NoConversationEvent()
    {
        TaskRecord task = (await Store().CreateAsync(new NewTask { Goal = "Briefing.", Trigger = TaskTrigger.Now() }, Ct)).Value;

        await Store().UpdateAsync(task with { Status = AgentTaskStatus.Done, Result = new TaskResult("Done.", "Done."), ResultAt = _time.GetUtcNow() }, Ct);

        Assert.DoesNotContain(_live.Events, live => live.Topic == "conversations");
    }

    private LiveTaskStore Store() => new(new EfTaskStore(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time), _live);
}
