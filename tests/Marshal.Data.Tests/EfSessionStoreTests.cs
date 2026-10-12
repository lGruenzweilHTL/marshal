using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;
using Marshal.Data.Conversations;
using Marshal.Data.Tokens;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class EfSessionStoreTests
{
    private static readonly IdentityEvidence _evidence = new("ha-user", "device-1", "assist_satellite.kitchen", "kitchen");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));

    [Fact]
    public async Task FindOpenByToken_SameTokenAndId_FindsSession()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        ApiTokenId token = await SeedTokenAsync(database, OwnerA);
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfSessionStore(database.Factory, database.Accessor, _time);
        SessionRecord created = await store.CreateAsync(new NewSession(SessionSource.Voice, token, "01JABC", _evidence, "owner_default"), Ct);

        SessionRecord? found = await store.FindOpenByTokenAsync(token, "01JABC", Ct);

        Assert.Equal(created.Id, found?.Id);
        Assert.Equal(OwnerA, found?.OwnerId);
        Assert.Equal("owner_default", found?.ResolvedBy);
    }

    [Fact]
    public async Task FindOpenByToken_SameIdOtherToken_NotFound()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        ApiTokenId first = await SeedTokenAsync(database, OwnerA);
        ApiTokenId second = await SeedTokenAsync(database, OwnerA);
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfSessionStore(database.Factory, database.Accessor, _time);
        await store.CreateAsync(new NewSession(SessionSource.Voice, first, "01JABC", _evidence, "owner_default"), Ct);

        Assert.Null(await store.FindOpenByTokenAsync(second, "01JABC", Ct));
    }

    [Fact]
    public async Task Create_SameTokenAndIdAfterClose_Allowed()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        ApiTokenId token = await SeedTokenAsync(database, OwnerA);
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfSessionStore(database.Factory, database.Accessor, _time);
        SessionRecord first = await store.CreateAsync(new NewSession(SessionSource.Voice, token, "01JABC", _evidence, "owner_default"), Ct);
        await store.CloseAsync(first.Id, Ct);

        SessionRecord second = await store.CreateAsync(new NewSession(SessionSource.Voice, token, "01JABC", _evidence, "owner_default"), Ct);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(second.Id, (await store.FindOpenByTokenAsync(token, "01JABC", Ct))?.Id);
        Assert.Null(await store.FindOpenAsync(first.Id, Ct));
    }

    [Fact]
    public async Task Create_StoresEvidenceAndNoPerTurnData()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfSessionStore(database.Factory, database.Accessor, _time);

        SessionRecord created = await store.CreateAsync(new NewSession(SessionSource.Web, null, null, _evidence, "cookie"), Ct);

        Session row = await context.Sessions.SingleAsync(Ct);
        Assert.Contains("assist_satellite.kitchen", row.IdentityEvidence, StringComparison.Ordinal);
        Assert.Contains("ha-user", row.IdentityEvidence, StringComparison.Ordinal);
        Assert.Equal(_time.GetUtcNow(), row.CreatedAt);
        Assert.Null(row.ApiTokenId);
        Assert.Equal(created.Id, row.Id);
    }

    [Fact]
    public async Task FindOpen_OtherOwnersSession_NotFound()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        SessionId id;
        await using (MarshalDbContext asA = database.CreateContext(OwnerA))
            id = (await new EfSessionStore(database.Factory, database.Accessor, _time).CreateAsync(new NewSession(SessionSource.Web, null, null, _evidence, "cookie"), Ct)).Id;

        await using MarshalDbContext asB = database.CreateContext(OwnerB);

        Assert.Null(await new EfSessionStore(database.Factory, database.Accessor, _time).FindOpenAsync(id, Ct));
    }

    [Fact]
    public async Task FindIdle_ReturnsOnlyOpenSessionsPastIdleTime()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfSessionStore(database.Factory, database.Accessor, _time);
        SessionRecord old = await store.CreateAsync(new NewSession(SessionSource.Web, null, null, _evidence, "cookie"), Ct);
        _time.Advance(TimeSpan.FromMinutes(10));
        SessionRecord fresh = await store.CreateAsync(new NewSession(SessionSource.Web, null, null, _evidence, "cookie"), Ct);

        IReadOnlyList<SessionRecord> idle = await store.FindIdleAsync(_time.GetUtcNow().AddMinutes(-5), Ct);

        Assert.Equal([old.Id], idle.Select(session => session.Id));
        Assert.Equal(SessionSource.Web, idle[0].Source);
        await store.TouchAsync(old.Id, Ct);
        Assert.Empty(await store.FindIdleAsync(_time.GetUtcNow().AddMinutes(-5), Ct));
        Assert.NotEqual(old.Id, fresh.Id);
    }

    [Fact]
    public async Task Rest_SkippedByFindIdle_ClearedByTouch()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        await using MarshalDbContext context = database.CreateContext(OwnerA);
        var store = new EfSessionStore(database.Factory, database.Accessor, _time);
        SessionRecord session = await store.CreateAsync(new NewSession(SessionSource.Web, null, null, _evidence, "cookie"), Ct);
        _time.Advance(TimeSpan.FromMinutes(10));

        await store.RestAsync(session.Id, Ct);

        Assert.Empty(await store.FindIdleAsync(_time.GetUtcNow().AddMinutes(-5), Ct));
        Assert.Equal(_time.GetUtcNow(), (await store.FindOpenAsync(session.Id, Ct))!.RestedAt);
        await store.TouchAsync(session.Id, Ct);
        Assert.Null((await store.FindOpenAsync(session.Id, Ct))!.RestedAt);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<ApiTokenId> SeedTokenAsync(TestDatabase database, OwnerId owner)
    {
        await using MarshalDbContext context = database.CreateContext(owner);
        var token = new ApiToken
        {
            Id = ApiTokenId.New(),
            OwnerId = owner,
            Name = "ha",
            TokenHash = Guid.NewGuid().ToString("N"),
            Scopes = ["conversation:write"],
            CreatedAt = DateTimeOffset.UnixEpoch,
        };
        context.ApiTokens.Add(token);
        await context.SaveChangesAsync();
        return token.Id;
    }
}
