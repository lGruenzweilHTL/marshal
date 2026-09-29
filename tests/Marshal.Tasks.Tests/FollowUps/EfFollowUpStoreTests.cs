using Marshal.Abstractions.Execution;
using Marshal.Abstractions.FollowUps;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Data;
using Marshal.Data.Tasks;
using Marshal.Tasks.FollowUps;
using Marshal.Tasks.Notifications;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.FollowUps;

public sealed class EfFollowUpStoreTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Voice(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Create_StoresRecordWithFreshNonceAndExpiry()
    {
        FollowUpRecord first = await Store().CreateAsync(new NewFollowUp("assist_satellite.office"), Ct);
        FollowUpRecord second = await Store().CreateAsync(new NewFollowUp("assist_satellite.office"), Ct);

        Assert.NotEqual(first.Nonce, second.Nonce);
        Assert.True(first.Nonce.Length >= 20);
        Assert.Equal($"marshal-followup:{first.Nonce}", first.Prompt);
        Assert.Equal(_time.GetUtcNow().AddMinutes(2), first.ExpiresAt);
        await using MarshalDbContext db = _database.CreateContext(OwnerA);
        FollowUp stored = await db.FollowUps.SingleAsync(row => row.Nonce == first.Nonce, Ct);
        Assert.Equal((OwnerA, "assist_satellite.office", (DateTimeOffset?)null), (stored.OwnerId, stored.SatelliteId, stored.ConsumedAt));
    }

    [Fact]
    public async Task Consume_ValidReference_OnceAndLinkedToTheSession()
    {
        FollowUpRecord asked = await Store().CreateAsync(new NewFollowUp(Office), Ct);
        SessionId session = await SessionAsync();

        FollowUpRecord? consumed = await Store().ConsumeAsync(asked.Nonce, Office, session, Ct);
        FollowUpRecord? again = await Store().ConsumeAsync(asked.Nonce, Office, session, Ct);

        Assert.Equal(_time.GetUtcNow(), consumed?.ConsumedAt);
        Assert.Null(again);
        await using MarshalDbContext db = _database.CreateContext(OwnerA);
        Assert.Equal(session, (await db.FollowUps.SingleAsync(row => row.Nonce == asked.Nonce, Ct)).LinkedSessionId);
    }

    [Fact]
    public async Task Consume_InvalidReferences_Ignored()
    {
        FollowUpRecord asked = await Store().CreateAsync(new NewFollowUp(Office), Ct);
        SessionId session = await SessionAsync();

        Assert.Null(await Store().ConsumeAsync("unknown-nonce", Office, session, Ct));
        Assert.Null(await Store().ConsumeAsync(asked.Nonce, "assist_satellite.kitchen", session, Ct));
        _database.Accessor.Current = Voice(OwnerB);
        Assert.Null(await Store().ConsumeAsync(asked.Nonce, Office, session, Ct));
        _database.Accessor.Current = Voice(OwnerA);
        _time.Advance(TimeSpan.FromMinutes(2));
        Assert.Null(await Store().ConsumeAsync(asked.Nonce, Office, session, Ct));
    }

    [Fact]
    public async Task ConsumeRecent_NewestWithinTheReplyWindowOnTheSatellite()
    {
        await Store().CreateAsync(new NewFollowUp(Office), Ct);
        _time.Advance(TimeSpan.FromSeconds(5));
        FollowUpRecord newest = await Store().CreateAsync(new NewFollowUp(Office), Ct);
        await Store().CreateAsync(new NewFollowUp("assist_satellite.kitchen"), Ct);
        _time.Advance(TimeSpan.FromSeconds(10));
        SessionId session = await SessionAsync();

        Assert.Null(await Store().ConsumeRecentAsync(Office, TimeSpan.FromSeconds(5), session, Ct));
        Assert.Equal(newest.Id, (await Store().ConsumeRecentAsync(Office, TimeSpan.FromSeconds(20), session, Ct))?.Id);
    }

    private const string Office = "assist_satellite.office";

    private async Task<SessionId> SessionAsync()
    {
        var sessions = new Marshal.Data.Conversations.EfSessionStore(_database.Factory, _database.Accessor, _time);
        return (await sessions.CreateAsync(new NewSession(SessionSource.Voice, null, null, new IdentityEvidence(null, null, Office, null), "test"), Ct)).Id;
    }

    private EfFollowUpStore Store() => new(_database.Factory, _database.Accessor, Options.Create(new NotificationOptions()), _time);
}
