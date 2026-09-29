using System.Text.Json;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Testing;

namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class HaStateConditionEvaluatorTests
{
    private readonly FixtureCache _cache = new();
    private readonly Preferences _preferences = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AllHold_EveryConditionMustHold()
    {
        SetState("binary_sensor.kitchen_occupancy", "on");
        SetState("zone.home", "1");

        Assert.True(await HoldAsync("state(binary_sensor.kitchen_occupancy = on)", "state(zone.home > 0)"));
        SetState("zone.home", "0");
        Assert.False(await HoldAsync("state(binary_sensor.kitchen_occupancy = on)", "state(zone.home > 0)"));
    }

    [Fact]
    public async Task Person_ResolvesToOwnersPersonEntity()
    {
        SetState("person.test_owner", "home");
        _preferences.Person = "person.test_owner";

        Assert.True(await HoldAsync("state($person = home)"));
        SetState("person.test_owner", "not_home");
        Assert.False(await HoldAsync("state($person = home)"));
    }

    [Fact]
    public async Task Person_NotConfigured_DoesNotHold()
    {
        SetState("person.test_owner", "home");

        Assert.False(await HoldAsync("state($person = home)"));
    }

    [Fact]
    public async Task UnknownEntityOrDisconnected_DoesNotHold()
    {
        Assert.False(await HoldAsync("state(binary_sensor.attic_occupancy != on)"));

        SetState("binary_sensor.kitchen_occupancy", "on");
        _cache.Current = null;
        Assert.False(await HoldAsync("state(binary_sensor.kitchen_occupancy = on)"));
    }

    private Task<bool> HoldAsync(params string[] conditions) =>
        new HaStateConditionEvaluator(_cache, _preferences).AllHoldAsync([.. conditions.Select(condition => StateCondition.Parse(condition).Value)], TestExecutionContext.OwnerA, Ct);

    private void SetState(string entity, string state) =>
        _cache.Current = _cache.Current! with { States = _cache.Current.States.SetItem(entity, new HaState(entity, state, JsonDocument.Parse("{}").RootElement, null)) };

    private sealed class Preferences : IUserPreferencesStore
    {
        public string? Person { get; set; }

        public Task<UserPreferencesRecord> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult(new UserPreferencesRecord("en", "Europe/Vienna", [], new NotificationTargets(Person, []), null));

        public Task SaveAsync(OwnerId owner, UserPreferencesRecord preferences, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
