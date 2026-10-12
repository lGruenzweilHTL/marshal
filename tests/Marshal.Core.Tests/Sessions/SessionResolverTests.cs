using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Sessions;
using Marshal.Abstractions.Tokens;
using Marshal.Abstractions.Tools;
using Marshal.Core.Execution;
using Marshal.Core.Sessions;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Core.Tests.Sessions;

public sealed class SessionResolverTests
{
    private static readonly IdentityEvidence _evidence = new(null, "device-1", "assist_satellite.kitchen", "kitchen");

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeExecutionContextAccessor _accessor = new();
    private readonly InMemorySessionStore _sessions;
    private readonly InMemoryPendingActionStore _pending;
    private readonly CountingResolver _identity = new();
    private readonly ApiTokenId _token = ApiTokenId.New();
    private readonly RecordingCloseListener _listener = new();

    public SessionResolverTests()
    {
        _sessions = new InMemorySessionStore(_accessor, _time);
        _pending = new InMemoryPendingActionStore(_time);
    }

    [Fact]
    public async Task Token_NewConversation_ResolvesOwnerAndStoresEvidence()
    {
        SessionRecord session = await Resolver().ResolveTokenSessionAsync(_token, OwnerA, "01JABC", _evidence, SessionSource.Voice, Ct);

        Assert.Equal(OwnerA, session.OwnerId);
        Assert.Equal("owner_default", session.ResolvedBy);
        Assert.Equal(_token, session.ApiTokenId);
        Assert.Equal("01JABC", session.ExternalId);
        Assert.Equal(_evidence, _sessions.EvidenceOf(session.Id));
        Assert.Equal(1, _identity.Calls);
    }

    [Fact]
    public async Task Token_SecondTurnBeforeTtl_ContinuesSessionWithoutReResolving()
    {
        SessionResolver resolver = Resolver();
        SessionRecord first = await resolver.ResolveTokenSessionAsync(_token, OwnerA, "01JABC", _evidence, SessionSource.Voice, Ct);
        _time.Advance(TimeSpan.FromMinutes(4));

        SessionRecord second = await resolver.ResolveTokenSessionAsync(_token, OwnerA, "01JABC", _evidence, SessionSource.Voice, Ct);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, _identity.Calls);
    }

    [Fact]
    public async Task Token_SameIdDifferentToken_SeparateSessions()
    {
        SessionResolver resolver = Resolver();

        SessionRecord first = await resolver.ResolveTokenSessionAsync(_token, OwnerA, "01JABC", _evidence, SessionSource.Voice, Ct);
        SessionRecord second = await resolver.ResolveTokenSessionAsync(ApiTokenId.New(), OwnerA, "01JABC", _evidence, SessionSource.Api, Ct);

        Assert.NotEqual(first.Id, second.Id);
    }

    [Fact]
    public async Task Token_TurnAfterIdleTtl_NewSessionAndOldPendingActionDropped()
    {
        SessionResolver resolver = Resolver();
        SessionRecord first = await resolver.ResolveTokenSessionAsync(_token, OwnerA, "01JABC", _evidence, SessionSource.Voice, Ct);
        PendingActionRecord pending = await _pending.FreezeAsync(new FreezeRequest(first.Id, null, new ToolId("ha", "x"), "{}", "x", [], "en", _time.GetUtcNow().AddMinutes(1)), Ct);
        _time.Advance(TimeSpan.FromMinutes(6));

        SessionRecord second = await resolver.ResolveTokenSessionAsync(_token, OwnerA, "01JABC", _evidence, SessionSource.Voice, Ct);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Contains(first.Id, _sessions.Closed);
        Assert.Equal(PendingActionStatus.Dropped, _pending.Decisions[pending.Id].Status);
        Assert.Equal(2, _identity.Calls);
        Assert.Equal([(OwnerA, first.Id)], _listener.Closed);
    }

    [Fact]
    public async Task Own_NoId_CreatesSessionForOwner()
    {
        Result<SessionRecord> session = await Resolver().ResolveOwnSessionAsync(null, OwnerB, SessionSource.Web, Ct);

        Assert.Equal(OwnerB, session.Value.OwnerId);
        Assert.Null(session.Value.ExternalId);
        Assert.Equal(0, _identity.Calls);
    }

    [Fact]
    public async Task Own_ExistingId_Continues()
    {
        SessionResolver resolver = Resolver();
        SessionRecord first = (await resolver.ResolveOwnSessionAsync(null, OwnerA, SessionSource.Web, Ct)).Value;

        Result<SessionRecord> again = await resolver.ResolveOwnSessionAsync(first.Id, OwnerA, SessionSource.Web, Ct);

        Assert.Equal(first.Id, again.Value.Id);
    }

    [Fact]
    public async Task Own_OtherOwnersId_NotFound()
    {
        SessionResolver resolver = Resolver();
        SessionRecord others = (await resolver.ResolveOwnSessionAsync(null, OwnerA, SessionSource.Web, Ct)).Value;

        Result<SessionRecord> result = await resolver.ResolveOwnSessionAsync(others.Id, OwnerB, SessionSource.Web, Ct);

        Assert.Equal("session.not_found", result.Error.Code);
        Assert.Equal(ErrorType.NotFound, result.Error.Type);
    }

    [Fact]
    public async Task Own_IdleWebSession_RestedThenContinued()
    {
        SessionResolver resolver = Resolver();
        SessionRecord first = (await resolver.ResolveOwnSessionAsync(null, OwnerA, SessionSource.Web, Ct)).Value;
        PendingActionRecord pending = await _pending.FreezeAsync(new FreezeRequest(first.Id, null, new ToolId("ha", "x"), "{}", "x", [], "en", _time.GetUtcNow().AddMinutes(10)), Ct);
        _time.Advance(TimeSpan.FromMinutes(6));

        Result<SessionRecord> result = await resolver.ResolveOwnSessionAsync(first.Id, OwnerA, SessionSource.Web, Ct);

        Assert.Equal(first.Id, result.Value.Id);
        Assert.Empty(_sessions.Closed);
        Assert.Equal(PendingActionStatus.Dropped, _pending.Decisions[pending.Id].Status);
        Assert.Equal([(OwnerA, first.Id)], _listener.Closed);
        Assert.Null(_sessions.All.Single().RestedAt);
        Assert.Equal(_time.GetUtcNow(), _sessions.All.Single().LastTurnAt);
    }

    [Fact]
    public async Task Own_RestedWebSession_ContinuedWithoutRestingAgain()
    {
        SessionResolver resolver = Resolver();
        SessionRecord first = (await resolver.ResolveOwnSessionAsync(null, OwnerA, SessionSource.Web, Ct)).Value;
        _time.Advance(TimeSpan.FromMinutes(6));
        await Maintenance().CloseIdleAsync(OwnerA, Ct);
        _time.Advance(TimeSpan.FromDays(2));

        Result<SessionRecord> result = await resolver.ResolveOwnSessionAsync(first.Id, OwnerA, SessionSource.Web, Ct);

        Assert.Equal(first.Id, result.Value.Id);
        Assert.Equal([(OwnerA, first.Id)], _listener.Closed);
        Assert.Null(_sessions.All.Single().RestedAt);
    }

    [Fact]
    public async Task Own_ClosedSession_NotFound()
    {
        SessionResolver resolver = Resolver();
        SessionRecord first = (await resolver.ResolveOwnSessionAsync(null, OwnerA, SessionSource.Web, Ct)).Value;
        using (_accessor.Use(Text(OwnerA)))
            await _sessions.CloseAsync(first.Id, Ct);

        Result<SessionRecord> result = await resolver.ResolveOwnSessionAsync(first.Id, OwnerA, SessionSource.Web, Ct);

        Assert.Equal("session.not_found", result.Error.Code);
    }

    [Fact]
    public async Task InstanceOwnerResolver_AlwaysReturnsInstanceOwner()
    {
        var resolver = new InstanceOwnerIdentityResolver(new FixedInstanceOwner(OwnerB));

        IdentityResolution resolution = await resolver.ResolveAsync(new IdentityEvidence("someone", "d", "s", "a"), Ct);

        Assert.Equal(new IdentityResolution(OwnerB, "owner_default"), resolution);
    }

    [Fact]
    public async Task IdleClose_ClosesIdleTokenSessionsAndDropsPendingActions()
    {
        SessionResolver resolver = Resolver();
        SessionRecord idle = await resolver.ResolveTokenSessionAsync(_token, OwnerA, "01JABC", _evidence, SessionSource.Voice, Ct);
        PendingActionRecord pending = await _pending.FreezeAsync(new FreezeRequest(idle.Id, null, new ToolId("ha", "x"), "{}", "x", [], "en", _time.GetUtcNow().AddMinutes(1)), Ct);
        _time.Advance(TimeSpan.FromMinutes(4));
        SessionRecord active = await resolver.ResolveTokenSessionAsync(_token, OwnerA, "01JDEF", _evidence, SessionSource.Voice, Ct);
        _time.Advance(TimeSpan.FromMinutes(2));

        int handled = await Maintenance().CloseIdleAsync(OwnerA, Ct);

        Assert.Equal(1, handled);
        Assert.Equal([idle.Id], _sessions.Closed);
        Assert.Equal(PendingActionStatus.Dropped, _pending.Decisions[pending.Id].Status);
        Assert.NotEqual(idle.Id, active.Id);
        Assert.Equal([(OwnerA, idle.Id)], _listener.Closed);
    }

    [Fact]
    public async Task IdleClose_RestsIdleWebSessionOnce()
    {
        SessionRecord idle = (await Resolver().ResolveOwnSessionAsync(null, OwnerA, SessionSource.Web, Ct)).Value;
        PendingActionRecord pending = await _pending.FreezeAsync(new FreezeRequest(idle.Id, null, new ToolId("ha", "x"), "{}", "x", [], "en", _time.GetUtcNow().AddMinutes(10)), Ct);
        _time.Advance(TimeSpan.FromMinutes(6));
        SessionMaintenance maintenance = Maintenance();

        int first = await maintenance.CloseIdleAsync(OwnerA, Ct);
        _time.Advance(TimeSpan.FromMinutes(6));
        int second = await maintenance.CloseIdleAsync(OwnerA, Ct);

        Assert.Equal(1, first);
        Assert.Equal(0, second);
        Assert.Empty(_sessions.Closed);
        Assert.NotNull(_sessions.All.Single().RestedAt);
        Assert.Equal(PendingActionStatus.Dropped, _pending.Decisions[pending.Id].Status);
        Assert.Equal([(OwnerA, idle.Id)], _listener.Closed);
    }

    [Fact]
    public async Task AsyncLocalAccessor_FlowsAcrossAwaitsAndRestores()
    {
        var accessor = new AsyncLocalExecutionContextAccessor();
        MarshalExecutionContext outer = Text(OwnerA);
        MarshalExecutionContext inner = Text(OwnerB);

        using (accessor.Use(outer))
        {
            await Task.Yield();
            Assert.Same(outer, accessor.Current);
            using (accessor.Use(inner))
            {
                await Task.Run(() => Assert.Same(inner, accessor.Current), Ct);
            }

            Assert.Same(outer, accessor.Current);
        }

        Assert.Null(accessor.Current);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private SessionMaintenance Maintenance() => new(_sessions, _pending, [_listener], _accessor, Options.Create(new SessionOptions()), _time);

    private SessionResolver Resolver() => new(_sessions, _identity, _pending, [_listener], _accessor, Options.Create(new SessionOptions()), _time);

    private sealed class RecordingCloseListener : ISessionCloseListener
    {
        public List<(OwnerId Owner, SessionId Session)> Closed { get; } = [];

        public Task SessionClosedAsync(OwnerId owner, SessionId session, CancellationToken cancellationToken)
        {
            Closed.Add((owner, session));
            return Task.CompletedTask;
        }
    }

    private sealed class CountingResolver : IIdentityResolver
    {
        public int Calls { get; private set; }

        public Task<IdentityResolution> ResolveAsync(IdentityEvidence evidence, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new IdentityResolution(OwnerA, "owner_default"));
        }
    }

    private sealed class FixedInstanceOwner(OwnerId owner) : IInstanceOwner
    {
        public Task<OwnerId> GetAsync(CancellationToken cancellationToken) => Task.FromResult(owner);
    }
}
