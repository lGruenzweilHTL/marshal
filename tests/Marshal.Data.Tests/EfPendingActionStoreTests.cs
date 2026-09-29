using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Data.Conversations;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class EfPendingActionStoreTests
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task Freeze_StoresActionWithReasonsNonceAndExpiry()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId session = await SeedSessionAsync(database, OwnerA);
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfPendingActionStore(database.Factory, database.Accessor, _time);

        PendingActionRecord frozen = await store.FreezeAsync(Request(session, """{"name":"front door"}"""), Ct);

        PendingActionRecord? loaded = await store.GetPendingAsync(session, Ct);
        Assert.NotNull(loaded);
        Assert.Equal(frozen.Id, loaded.Id);
        Assert.Equal(OwnerA, loaded.OwnerId);
        Assert.Equal("""{"name":"front door"}""", loaded.ArgumentsJson);
        Assert.Equal(_time.GetUtcNow().AddSeconds(60), loaded.ExpiresAt);
        Assert.Equal(ConfirmationReasonCode.Taint, loaded.Reasons[0].Code);
        Assert.Equal("taint sentence", loaded.Reasons[0].Sentence);
        Assert.True(loaded.Nonce.Length >= 32);
    }

    [Fact]
    public async Task Freeze_SecondInSession_SupersedesFirst()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId session = await SeedSessionAsync(database, OwnerA);
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfPendingActionStore(database.Factory, database.Accessor, _time);

        PendingActionRecord first = await store.FreezeAsync(Request(session, "{}"), Ct);
        PendingActionRecord second = await store.FreezeAsync(Request(session, """{"n":2}"""), Ct);

        Assert.Equal(second.Id, (await store.GetPendingAsync(session, Ct))?.Id);
        PendingAction firstRow = await context.PendingActions.SingleAsync(action => action.Id == first.Id, Ct);
        Assert.Equal(PendingActionStatus.Superseded, firstRow.Status);
        Assert.Equal(DecidedBy.System, firstRow.DecidedBy);
        Assert.NotEqual(first.Nonce, second.Nonce);
    }

    [Fact]
    public async Task Decide_OnlyFromPending()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId session = await SeedSessionAsync(database, OwnerA);
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfPendingActionStore(database.Factory, database.Accessor, _time);
        PendingActionRecord frozen = await store.FreezeAsync(Request(session, "{}"), Ct);

        Result approved = await store.DecideAsync(frozen.Id, PendingActionStatus.Approved, DecidedBy.Keyword, Ct);
        Result again = await store.DecideAsync(frozen.Id, PendingActionStatus.Approved, DecidedBy.Keyword, Ct);

        Assert.True(approved.IsSuccess);
        Assert.Equal("pending_action.not_pending", again.Error.Code);
        Assert.Null(await store.GetPendingAsync(session, Ct));
        PendingAction row = await context.PendingActions.SingleAsync(Ct);
        Assert.Equal(DecidedBy.Keyword, row.DecidedBy);
        Assert.Equal(_time.GetUtcNow(), row.DecidedAt);
    }

    [Fact]
    public async Task DropForSession_MarksDropped()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId session = await SeedSessionAsync(database, OwnerA);
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfPendingActionStore(database.Factory, database.Accessor, _time);
        await store.FreezeAsync(Request(session, "{}"), Ct);

        await store.DropForSessionAsync(session, Ct);

        Assert.Null(await store.GetPendingAsync(session, Ct));
        Assert.Equal(PendingActionStatus.Dropped, (await context.PendingActions.SingleAsync(Ct)).Status);
    }

    [Fact]
    public async Task GetPending_OtherOwner_SeesNothing()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId session = await SeedSessionAsync(database, OwnerA);
        await using (MarshalDbContext context = database.CreateContext(OwnerA))
            await new EfPendingActionStore(database.Factory, database.Accessor, _time).FreezeAsync(Request(session, "{}"), Ct);

        await using MarshalDbContext asB = database.CreateContext(OwnerB);
        var storeB = new EfPendingActionStore(database.Factory, database.Accessor, _time);

        Assert.Null(await storeB.GetPendingAsync(session, Ct));
    }

    [Fact]
    public async Task Freeze_BackgroundApproval_KeepsTaskRunAndTaint()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        database.Accessor.Current = Text(OwnerA);
        var task = TaskId.New();
        await using (MarshalDbContext context = database.CreateContext(OwnerA))
        {
            context.AgentTasks.Add(new Tasks.AgentTask
            {
                Id = task,
                OwnerId = OwnerA,
                CreatedBy = TaskCreator.User,
                Kind = TaskKind.Agent,
                Goal = "Print the part.",
                TriggerKind = TaskTriggerKind.Now,
                TriggerSpec = "",
                CreatedAt = DateTimeOffset.UnixEpoch,
                UpdatedAt = DateTimeOffset.UnixEpoch,
            });
            await context.SaveChangesAsync(Ct);
        }

        var store = new EfPendingActionStore(database.Factory, database.Accessor, _time);
        var run = RunId.New();

        PendingActionRecord frozen = await store.FreezeAsync(Request(null, "{}") with { TaskId = task, RunId = run, Taint = [new TaintSource("mail", 3)] }, Ct);

        PendingActionRecord? loaded = await store.GetAsync(frozen.Id, Ct);
        Assert.NotNull(loaded);
        Assert.Equal((task, run, PendingActionStatus.Pending), (loaded.TaskId, loaded.RunId, loaded.Status));
        Assert.Equal([new TaintSource("mail", 3)], loaded.Taint);
        database.Accessor.Current = Text(OwnerB);
        Assert.Null(await new EfPendingActionStore(database.Factory, database.Accessor, _time).GetAsync(frozen.Id, Ct));
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private FreezeRequest Request(SessionId? session, string arguments) => new(
        session,
        "assist_satellite.kitchen",
        new ToolId("ha", "HassTurnOff"),
        arguments,
        "unlock the front door",
        [new ConfirmationReason(ConfirmationReasonCode.Taint, "taint sentence"), new ConfirmationReason(ConfirmationReasonCode.Rule, "rule sentence")],
        "en",
        _time.GetUtcNow().AddSeconds(60));

    private static async Task<SessionId> SeedSessionAsync(TestDatabase database, Abstractions.Identity.OwnerId owner)
    {
        await using MarshalDbContext context = database.CreateContext(owner);
        var session = new Session
        {
            Id = SessionId.New(),
            OwnerId = owner,
            Source = SessionSource.Voice,
            IdentityEvidence = "{}",
            ResolvedBy = "owner_default",
            CreatedAt = DateTimeOffset.UnixEpoch,
            LastTurnAt = DateTimeOffset.UnixEpoch,
        };
        context.Sessions.Add(session);
        await context.SaveChangesAsync();
        return session.Id;
    }
}
