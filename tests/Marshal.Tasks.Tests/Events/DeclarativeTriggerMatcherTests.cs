using System.Text.Json;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Events;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Events;

public sealed class DeclarativeTriggerMatcherTests : IAsyncLifetime
{
    private const string AliceHome = """{"source":"ha","entity":"person.alice","to":"home"}""";

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly TaskSignals _signals = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateAsync();

    public async ValueTask DisposeAsync()
    {
        _signals.Dispose();
        await _database.DisposeAsync();
    }

    [Fact]
    public async Task Match_WatchFires_DueWithNormalizedEventAsPayload()
    {
        TaskRecord watch = await CreateAsync(OwnerA, TaskTrigger.Event(AliceHome));

        await Matcher().MatchAsync(StateChanged("person.alice", "not_home", "home"), Ct);

        TaskRecord fired = await GetAsync(OwnerA, watch.Id);
        Assert.Equal((AgentTaskStatus.Pending, _time.GetUtcNow()), (fired.Status, fired.NextRunAt));
        using var payload = JsonDocument.Parse(fired.TriggerPayload!);
        Assert.Equal("ha", payload.RootElement.GetProperty("source").GetString());
        Assert.Equal("state_changed", payload.RootElement.GetProperty("type").GetString());
        Assert.Equal("person.alice", payload.RootElement.GetProperty("entity").GetString());
        Assert.Equal("home", payload.RootElement.GetProperty("payload").GetProperty("to").GetString());
        Assert.True(_signals.WaitAsync(Ct).IsCompleted);
    }

    [Fact]
    public async Task Match_NonMatchingEvent_NothingChanges()
    {
        TaskRecord watch = await CreateAsync(OwnerA, TaskTrigger.Event(AliceHome));

        await Matcher().MatchAsync(StateChanged("person.alice", "home", "not_home"), Ct);
        await Matcher().MatchAsync(StateChanged("person.bob", "not_home", "home"), Ct);

        TaskRecord unchanged = await GetAsync(OwnerA, watch.Id);
        Assert.Equal((watch.Status, watch.NextRunAt, watch.TriggerPayload, watch.RowVersion), (unchanged.Status, unchanged.NextRunAt, unchanged.TriggerPayload, unchanged.RowVersion));
        Assert.False(_signals.WaitAsync(Ct).IsCompleted);
    }

    [Fact]
    public async Task Match_AlreadyFired_FiresOncePerArming()
    {
        TaskRecord watch = await CreateAsync(OwnerA, TaskTrigger.Event(AliceHome));
        await Matcher().MatchAsync(StateChanged("person.alice", "not_home", "home"), Ct);
        string? first = (await GetAsync(OwnerA, watch.Id)).TriggerPayload;
        _time.Advance(TimeSpan.FromMinutes(1));

        await Matcher().MatchAsync(StateChanged("person.alice", "not_home", "home"), Ct);

        TaskRecord fired = await GetAsync(OwnerA, watch.Id);
        Assert.Equal((first, _time.GetUtcNow().AddMinutes(-1)), (fired.TriggerPayload, fired.NextRunAt));
    }

    [Fact]
    public async Task Match_OwnedEvent_OnlyThatOwnersWatches()
    {
        const string Mail = """{"source":"mail","type":"new_mail"}""";
        TaskRecord alices = await CreateAsync(OwnerA, TaskTrigger.Event(Mail));
        TaskRecord bobs = await CreateAsync(OwnerB, TaskTrigger.Event(Mail));

        await Matcher().MatchAsync(new MarshalEvent("mail", "new_mail", null, JsonDocument.Parse("{}").RootElement, OwnerB, _time.GetUtcNow()), Ct);

        Assert.Null((await GetAsync(OwnerA, alices.Id)).NextRunAt);
        Assert.NotNull((await GetAsync(OwnerB, bobs.Id)).NextRunAt);
    }

    [Fact]
    public async Task Match_InstanceEvent_EveryOwnersWatches()
    {
        TaskRecord alices = await CreateAsync(OwnerA, TaskTrigger.Event(AliceHome));
        TaskRecord bobs = await CreateAsync(OwnerB, TaskTrigger.Event(AliceHome));

        await Matcher().MatchAsync(StateChanged("person.alice", "not_home", "home"), Ct);

        Assert.NotNull((await GetAsync(OwnerA, alices.Id)).NextRunAt);
        Assert.NotNull((await GetAsync(OwnerB, bobs.Id)).NextRunAt);
    }

    [Fact]
    public async Task Match_RearmedWatch_FiresAgain()
    {
        TaskRecord watch = await CreateAsync(OwnerA, TaskTrigger.Event(AliceHome));
        await UpdateAsync(OwnerA, watch with { Status = AgentTaskStatus.Waiting, StatusReason = null });

        await Matcher().MatchAsync(StateChanged("person.alice", "not_home", "home"), Ct);

        TaskRecord fired = await GetAsync(OwnerA, watch.Id);
        Assert.Equal((AgentTaskStatus.Pending, _time.GetUtcNow()), (fired.Status, fired.NextRunAt));
    }

    [Fact]
    public async Task Match_CalendarInvitation_FiresTheWatch()
    {
        TaskRecord watch = await CreateAsync(OwnerA, TaskTrigger.Event("""{"source":"calendar","type":"invitation"}"""));
        TaskRecord created = await CreateAsync(OwnerA, TaskTrigger.Event("""{"source":"calendar","type":"event_created"}"""));
        JsonElement payload = JsonDocument.Parse("""{"calendar":"caldav:nextcloud/personal","eventId":"invite-1","start":"2026-10-22T08:00:00+00:00","end":"2026-10-22T09:00:00+00:00"}""").RootElement.Clone();

        await Matcher().MatchAsync(new MarshalEvent("calendar", "invitation", "caldav:nextcloud/personal", payload, OwnerA, _time.GetUtcNow(), Trusted: true), Ct);

        TaskRecord fired = await GetAsync(OwnerA, watch.Id);
        Assert.Equal((AgentTaskStatus.Pending, _time.GetUtcNow()), (fired.Status, fired.NextRunAt));
        using var stored = JsonDocument.Parse(fired.TriggerPayload!);
        Assert.Equal("invite-1", stored.RootElement.GetProperty("payload").GetProperty("eventId").GetString());
        Assert.Null((await GetAsync(OwnerA, created.Id)).NextRunAt);
    }

    private DeclarativeTriggerMatcher Matcher() =>
        new(Store(), _signals, _database.Accessor, _time, NullLogger<DeclarativeTriggerMatcher>.Instance);

    private EfTaskStore Store() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(new TaskOptions()), _time);

    private MarshalEvent StateChanged(string entity, string from, string to) =>
        new("ha", "state_changed", entity, JsonDocument.Parse($$"""{"from":"{{from}}","to":"{{to}}"}""").RootElement.Clone(), null, _time.GetUtcNow());

    private async Task<TaskRecord> CreateAsync(OwnerId owner, TaskTrigger trigger)
    {
        using IDisposable scope = _database.Accessor.Use(Text(owner));
        return (await Store().CreateAsync(new NewTask { Goal = "Greet.", Trigger = trigger }, Ct)).Value;
    }

    private async Task<TaskRecord> GetAsync(OwnerId owner, TaskId id)
    {
        using IDisposable scope = _database.Accessor.Use(Text(owner));
        return (await Store().GetAsync(id, Ct)).Value;
    }

    private async Task UpdateAsync(OwnerId owner, TaskRecord task)
    {
        using IDisposable scope = _database.Accessor.Use(Text(owner));
        Assert.True((await Store().UpdateAsync(task, Ct)).IsSuccess);
    }
}
