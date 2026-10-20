using Marshal.Abstractions.Identity;
using Marshal.Data.Identity;
using Marshal.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Data.Tests;

public sealed class EfUserPreferencesStoreTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Get_NothingStored_DefaultsToEnglishAndTheInstanceTimeZone()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();

        UserPreferencesRecord preferences = await Store(database).GetAsync(OwnerA, Ct);

        Assert.Equal(("en", "Europe/Vienna"), (preferences.Language, preferences.TimeZone));
        Assert.Empty(preferences.QuietHours);
        Assert.Null(preferences.NotificationTargets.Person);
        Assert.Empty(preferences.NotificationTargets.Push);
        Assert.Null(preferences.DndUntil);
        Assert.False(preferences.HelpMarshalLearn);
    }

    [Fact]
    public async Task Save_ThenGet_RoundTripsEveryField()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();
        var saved = new UserPreferencesRecord(
            "de",
            "America/New_York",
            [new QuietHoursWindow(DayOfWeek.Friday, new TimeOnly(23, 30), new TimeOnly(8, 0))],
            new NotificationTargets("person.lukas", ["notify.mobile_app_pixel"]),
            new DateTimeOffset(2026, 9, 28, 20, 0, 0, TimeSpan.Zero),
            HelpMarshalLearn: true);

        await Store(database).SaveAsync(OwnerA, saved, Ct);
        await Store(database).SaveAsync(OwnerA, saved, Ct);
        UserPreferencesRecord loaded = await Store(database).GetAsync(OwnerA, Ct);

        Assert.Equal(saved.Language, loaded.Language);
        Assert.Equal(saved.TimeZone, loaded.TimeZone);
        Assert.Equal(saved.QuietHours, loaded.QuietHours);
        Assert.Equal(saved.NotificationTargets.Person, loaded.NotificationTargets.Person);
        Assert.Equal(saved.NotificationTargets.Push, loaded.NotificationTargets.Push);
        Assert.Equal(saved.DndUntil, loaded.DndUntil);
        Assert.True(loaded.HelpMarshalLearn);
    }

    [Fact]
    public async Task Save_ForOneUser_LeavesOthersAlone()
    {
        await using TestDatabase database = await TestDatabase.CreateAsync();

        await Store(database).SaveAsync(OwnerA, new UserPreferencesRecord("de", "UTC", [], NotificationTargets.None, null), Ct);

        Assert.Equal("en", (await Store(database).GetAsync(OwnerB, Ct)).Language);
    }

    private static EfUserPreferencesStore Store(TestDatabase database) => new(database.Factory, new FixedTimeZone());

    private sealed class FixedTimeZone : IInstanceTimeZone
    {
        public Task<string> GetAsync(CancellationToken cancellationToken) => Task.FromResult("Europe/Vienna");
    }
}
