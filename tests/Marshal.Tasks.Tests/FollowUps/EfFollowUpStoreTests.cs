using Marshal.Abstractions.FollowUps;
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

    private EfFollowUpStore Store() => new(_database.Factory, _database.Accessor, Options.Create(new NotificationOptions()), _time);
}
