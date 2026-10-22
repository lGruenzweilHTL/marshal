using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Suggestions;
using Marshal.Tasks.Suggestions;
using Marshal.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Suggestions;

public sealed class EfSuggestionStoreTests : IAsyncLifetime
{
    private static readonly DateTimeOffset Now = new(2026, 10, 21, 8, 0, 0, TimeSpan.Zero);

    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _database = await TestDatabase.CreateAsync();

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Add_CountsSent_OwnerScoped()
    {
        await AsAsync(OwnerA, store => store.AddAsync(Record(OwnerA, "window_rain", "bedroom"), Ct));

        SuggestionDetectorStats alice = await AsAsync(OwnerA, store => store.GetStatsAsync("window_rain", Ct));
        SuggestionDetectorStats bob = await AsAsync(OwnerB, store => store.GetStatsAsync("window_rain", Ct));
        Assert.Equal((1, 0, 0), (alice.Sent, alice.Accepted, alice.Dismissed));
        Assert.Equal((0, 0, 0), (bob.Sent, bob.Accepted, bob.Dismissed));
        Assert.Single(await AsAsync(OwnerA, store => store.ListOpenAsync(Ct)));
        Assert.Empty(await AsAsync(OwnerB, store => store.ListOpenAsync(Ct)));
    }

    [Fact]
    public async Task SentSince_DedupeKeyWithinWindow()
    {
        await AsAsync(OwnerA, store => store.AddAsync(Record(OwnerA, "window_rain", "bedroom"), Ct));

        Assert.True(await AsAsync(OwnerA, store => store.SentSinceAsync("window_rain", "bedroom", Now.AddHours(-12), Ct)));
        Assert.False(await AsAsync(OwnerA, store => store.SentSinceAsync("window_rain", "bedroom", Now.AddMinutes(1), Ct)));
        Assert.False(await AsAsync(OwnerA, store => store.SentSinceAsync("window_rain", "kitchen", Now.AddHours(-12), Ct)));
        Assert.False(await AsAsync(OwnerB, store => store.SentSinceAsync("window_rain", "bedroom", Now.AddHours(-12), Ct)));
    }

    [Fact]
    public async Task Decide_AcceptAndDismissCount_ExpiredDoesNot()
    {
        SuggestionRecord accepted = Record(OwnerA, "window_rain", "a");
        SuggestionRecord dismissed = Record(OwnerA, "window_rain", "b");
        SuggestionRecord expired = Record(OwnerA, "window_rain", "c");
        foreach (SuggestionRecord record in new[] { accepted, dismissed, expired })
            await AsAsync(OwnerA, store => store.AddAsync(record, Ct));

        await AsAsync(OwnerA, store => store.DecideAsync(accepted.Id, SuggestionStatus.Accepted, Now, Ct));
        await AsAsync(OwnerA, store => store.DecideAsync(dismissed.Id, SuggestionStatus.Dismissed, Now, Ct));
        await AsAsync(OwnerA, store => store.DecideAsync(expired.Id, SuggestionStatus.Expired, Now, Ct));

        SuggestionDetectorStats stats = await AsAsync(OwnerA, store => store.GetStatsAsync("window_rain", Ct));
        Assert.Equal((3, 1, 1), (stats.Sent, stats.Accepted, stats.Dismissed));
        Assert.Empty(await AsAsync(OwnerA, store => store.ListOpenAsync(Ct)));
        Result<SuggestionRecord> again = await AsAsync(OwnerA, store => store.DecideAsync(accepted.Id, SuggestionStatus.Dismissed, Now, Ct));
        Assert.Equal(ErrorType.Conflict, again.Error.Type);
    }

    [Fact]
    public async Task Decide_OtherOwnersSuggestion_NotFound()
    {
        SuggestionRecord record = Record(OwnerA, "window_rain", "a");
        await AsAsync(OwnerA, store => store.AddAsync(record, Ct));

        Result<SuggestionRecord> decided = await AsAsync(OwnerB, store => store.DecideAsync(record.Id, SuggestionStatus.Accepted, Now, Ct));

        Assert.Equal(ErrorType.NotFound, decided.Error.Type);
    }

    [Fact]
    public async Task Mute_WithEndTime_AndUnmute()
    {
        await AsAsync(OwnerA, store => store.MuteAsync("energy_anomaly", Now.AddDays(7), Ct));

        SuggestionDetectorStats muted = await AsAsync(OwnerA, store => store.GetStatsAsync("energy_anomaly", Ct));
        Assert.True(muted.IsMuted(Now));
        Assert.False(muted.IsMuted(Now.AddDays(8)));
        Assert.False((await AsAsync(OwnerB, store => store.GetStatsAsync("energy_anomaly", Ct))).IsMuted(Now));

        await AsAsync(OwnerA, store => store.MuteAsync("energy_anomaly", DateTimeOffset.MaxValue, Ct));
        Assert.True((await AsAsync(OwnerA, store => store.GetStatsAsync("energy_anomaly", Ct))).IsMuted(Now.AddYears(50)));

        await AsAsync(OwnerA, store => store.MuteAsync("energy_anomaly", null, Ct));
        Assert.False((await AsAsync(OwnerA, store => store.GetStatsAsync("energy_anomaly", Ct))).IsMuted(Now));
        Assert.Equal(["energy_anomaly"], (await AsAsync(OwnerA, store => store.ListStatsAsync(Ct))).Select(stats => stats.Detector));
    }

    [Fact]
    public async Task Reset_ZerosCounts_KeepsMute()
    {
        SuggestionRecord record = Record(OwnerA, "energy_anomaly", "a");
        await AsAsync(OwnerA, store => store.AddAsync(record, Ct));
        await AsAsync(OwnerA, store => store.DecideAsync(record.Id, SuggestionStatus.Dismissed, Now, Ct));
        await AsAsync(OwnerA, store => store.MuteAsync("energy_anomaly", Now.AddDays(1), Ct));

        await AsAsync(OwnerA, store => store.ResetAsync("energy_anomaly", Ct));

        SuggestionDetectorStats stats = await AsAsync(OwnerA, store => store.GetStatsAsync("energy_anomaly", Ct));
        Assert.Equal((0, 0, 0, true), (stats.Sent, stats.Accepted, stats.Dismissed, stats.IsMuted(Now)));
    }

    [Fact]
    public async Task FindByNonce_AcrossOwners_OpenOnly()
    {
        SuggestionRecord record = Record(OwnerB, "print_done_away", "voron");
        await AsAsync(OwnerB, store => store.AddAsync(record, Ct));

        Assert.Equal((record.Id, OwnerB), await Store().FindByNonceAsync(record.Nonce, Ct));
        Assert.Null(await Store().FindByNonceAsync("unknown", Ct));
        Assert.Single(await Store().ListAllOpenAsync(Ct));

        await AsAsync(OwnerB, store => store.DecideAsync(record.Id, SuggestionStatus.Expired, Now, Ct));
        Assert.Null(await Store().FindByNonceAsync(record.Nonce, Ct));
        Assert.Empty(await Store().ListAllOpenAsync(Ct));
    }

    private static SuggestionRecord Record(OwnerId owner, string detector, string key) => new(
        Guid.CreateVersion7(), owner, detector, key, "Close the bedroom window, rain is coming.", new Dictionary<string, string> { ["window"] = "Bedroom window" },
        SuggestionDelivery.Inbox, SuggestionStatus.Open, Guid.NewGuid().ToString("N"), null, null, Now, Now.AddHours(6));

    private EfSuggestionStore Store() => new(_database.Factory, _database.Accessor);

    private async Task<T> AsAsync<T>(OwnerId owner, Func<EfSuggestionStore, Task<T>> action)
    {
        using IDisposable scope = _database.Accessor.Use(Text(owner));
        return await action(Store());
    }

    private async Task AsAsync(OwnerId owner, Func<EfSuggestionStore, Task> action)
    {
        using IDisposable scope = _database.Accessor.Use(Text(owner));
        await action(Store());
    }
}
