using Marshal.Abstractions.Health;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.LiveEvents;
using Marshal.Api.Health;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Api.Tests.Health;

public sealed class AggregatedSystemHealthTests
{
    private static readonly OwnerId _owner = OwnerId.New();

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 16, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeLiveEventPublisher _live = new();
    private readonly Provider _first = new();
    private readonly Provider _second = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task NoConditions_NoWarnings()
    {
        Assert.Empty(await Health().GetWarningsAsync(Ct));
    }

    [Fact]
    public async Task ProvidersConditions_AreCombinedErrorsFirst()
    {
        _first.Conditions = [Condition("mcp:paperless", "mcp.unreachable")];
        _second.Conditions = [Condition("database", "database.unreachable", HealthSeverity.Error)];

        IReadOnlyList<HealthWarning> warnings = await Health().GetWarningsAsync(Ct);

        Assert.Equal(["database.unreachable", "mcp.unreachable"], warnings.Select(warning => warning.Code));
        Assert.Equal(HealthSeverity.Error, warnings[0].Severity);
    }

    [Fact]
    public async Task Since_IsTheFirstTimeTheWarningAppeared()
    {
        AggregatedSystemHealth health = Health();
        _first.Conditions = [Condition("mcp:paperless", "mcp.unreachable")];
        DateTimeOffset first = _time.GetUtcNow();
        await health.GetWarningsAsync(Ct);

        _time.Advance(TimeSpan.FromMinutes(5));
        HealthWarning warning = Assert.Single(await health.GetWarningsAsync(Ct));

        Assert.Equal(first, warning.Since);
    }

    [Fact]
    public async Task Since_ResetsAfterTheWarningCleared()
    {
        AggregatedSystemHealth health = Health();
        _first.Conditions = [Condition("mcp:paperless", "mcp.unreachable")];
        await health.GetWarningsAsync(Ct);
        _first.Conditions = [];
        await health.GetWarningsAsync(Ct);

        _time.Advance(TimeSpan.FromMinutes(5));
        _first.Conditions = [Condition("mcp:paperless", "mcp.unreachable")];
        HealthWarning warning = Assert.Single(await health.GetWarningsAsync(Ct));

        Assert.Equal(_time.GetUtcNow(), warning.Since);
    }

    [Fact]
    public async Task FailingProvider_IsSkipped()
    {
        _first.Failure = new InvalidOperationException("boom");
        _second.Conditions = [Condition("database", "database.unreachable", HealthSeverity.Error)];

        HealthWarning warning = Assert.Single(await Health().GetWarningsAsync(Ct));

        Assert.Equal("database.unreachable", warning.Code);
    }

    [Fact]
    public async Task Change_IsPublishedOnHealthTopicToEveryUser()
    {
        AggregatedSystemHealth health = Health();
        await health.GetWarningsAsync(Ct);
        Assert.Empty(_live.Events);

        _first.Conditions = [Condition("mcp:paperless", "mcp.unreachable")];
        await health.GetWarningsAsync(Ct);

        LiveEvent published = Assert.Single(_live.Events);
        Assert.Equal("health", published.Topic);
        Assert.Equal("health.changed", published.Type);
        Assert.Equal(_owner, published.Owner);
        SystemHealthView view = Assert.IsType<SystemHealthView>(published.Payload);
        Assert.Equal("mcp.unreachable", Assert.Single(view.Warnings).Code);
    }

    [Fact]
    public async Task Unchanged_IsNotPublishedAgain()
    {
        AggregatedSystemHealth health = Health();
        _first.Conditions = [Condition("mcp:paperless", "mcp.unreachable")];
        await health.GetWarningsAsync(Ct);
        _time.Advance(TimeSpan.FromMinutes(1));

        await health.GetWarningsAsync(Ct);

        Assert.Single(_live.Events);
    }

    [Fact]
    public async Task ClearedWarnings_ArePublished()
    {
        AggregatedSystemHealth health = Health();
        _first.Conditions = [Condition("mcp:paperless", "mcp.unreachable")];
        await health.GetWarningsAsync(Ct);

        _first.Conditions = [];
        await health.GetWarningsAsync(Ct);

        Assert.Equal(2, _live.Events.Count);
        Assert.Empty(Assert.IsType<SystemHealthView>(_live.Events.Last().Payload).Warnings);
    }

    private AggregatedSystemHealth Health() =>
        new([_first, _second], _live, new Users(), _time, NullLogger<AggregatedSystemHealth>.Instance);

    private static HealthCondition Condition(string component, string code, HealthSeverity severity = HealthSeverity.Warning) =>
        new(component, code, severity, new HealthText($"{code} (de)", $"{code} (en)"));

    private sealed class Provider : IHealthWarningProvider
    {
        public IReadOnlyList<HealthCondition> Conditions { get; set; } = [];

        public Exception? Failure { get; set; }

        public ValueTask<IReadOnlyList<HealthCondition>> GetConditionsAsync(CancellationToken cancellationToken) =>
            Failure is { } failure ? ValueTask.FromException<IReadOnlyList<HealthCondition>>(failure) : ValueTask.FromResult(Conditions);
    }

    private sealed class Users : IUserDirectory
    {
        public Task<IReadOnlyList<OwnerId>> ListAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<OwnerId>>([_owner]);
    }
}
