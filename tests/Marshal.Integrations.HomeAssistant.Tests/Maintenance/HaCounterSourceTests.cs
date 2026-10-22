using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Maintenance;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.HomeAssistant.Tests.Maintenance;

public sealed class HaCounterSourceTests
{
    private readonly FixtureCache _cache = new();
    private readonly FakeStatistics _commands = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.Zero));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EntityState_NumberWithUnit()
    {
        Result<MaintenanceReading> reading = await Source().ReadAsync(new MaintenanceSource(Ha: "sensor.outside_temperature"), Ct);

        Assert.Equal(new MaintenanceReading(15.6m, "°C"), reading.Value);
        Assert.Empty(_commands.Requests);
    }

    [Fact]
    public async Task EntityState_NotANumber_Fails()
    {
        Result<MaintenanceReading> reading = await Source().ReadAsync(new MaintenanceSource(Ha: "person.test_owner"), Ct);

        Assert.Equal("maintenance.not_a_number", reading.Error.Code);
    }

    [Fact]
    public async Task Statistic_LatestSum()
    {
        Result<MaintenanceReading> reading = await Source().ReadAsync(new MaintenanceSource(Ha: "sensor:dishwasher_cycles"), Ct);

        Assert.Equal(new MaintenanceReading(61m, null), reading.Value);
        JsonObject request = Assert.Single(_commands.Requests);
        Assert.Equal(("hour", "sum", "sensor:dishwasher_cycles"), ((string?)request["period"], (string?)request["types"]![0], (string?)request["statistic_ids"]![0]));
    }

    [Fact]
    public async Task UnknownCounter_NotFound_Disconnected_Unavailable()
    {
        Assert.Equal("maintenance.counter_not_found", (await Source().ReadAsync(new MaintenanceSource(Ha: "sensor.nothing"), Ct)).Error.Code);

        _cache.Current = null;
        Assert.Equal("ha.unavailable", (await Source().ReadAsync(new MaintenanceSource(Ha: "sensor.outside_temperature"), Ct)).Error.Code);
    }

    [Fact]
    public async Task ListCounters_TotalIncreasingRuntimeSensors()
    {
        HaSnapshot recorded = FixtureCache.Recorded;
        _cache.Current = recorded with
        {
            States = recorded.States
                .SetItem("sensor.dishwasher_cycles", Sensor("sensor.dishwasher_cycles", "61", "Dishwasher cycles", "total_increasing", null))
                .SetItem("sensor.heat_pump", Sensor("sensor.heat_pump", "1234.5", "Wärmepumpe Betriebsstunden", "total_increasing", "h"))
                .SetItem("sensor.grid_import", Sensor("sensor.grid_import", "5000", "Grid import", "total_increasing", "kWh"))
                .SetItem("sensor.pump_runtime", Sensor("sensor.pump_runtime", "12", "Pump runtime", "measurement", "h"))
                .SetItem("sensor.filter_hours", Sensor("sensor.filter_hours", "unavailable", "Filter hours", "total_increasing", "h")),
        };

        IReadOnlyList<MaintenanceCounter> counters = await Source().ListCountersAsync(Ct);

        Assert.Equal(
            [
                new MaintenanceCounter(new MaintenanceSource(Ha: "sensor.dishwasher_cycles"), "Dishwasher cycles", new MaintenanceReading(61m, null)),
                new MaintenanceCounter(new MaintenanceSource(Ha: "sensor.filter_hours"), "Filter hours", null),
                new MaintenanceCounter(new MaintenanceSource(Ha: "sensor.heat_pump"), "Wärmepumpe Betriebsstunden", new MaintenanceReading(1234.5m, "h")),
            ],
            counters);

        _cache.Current = null;
        Assert.Empty(await Source().ListCountersAsync(Ct));
    }

    private HaState Sensor(string id, string state, string name, string stateClass, string? unit) =>
        new(id, state, JsonSerializer.SerializeToElement(new Dictionary<string, string?> { ["friendly_name"] = name, ["state_class"] = stateClass, ["unit_of_measurement"] = unit }), _time.GetUtcNow());

    private HaCounterSource Source() => new(_cache, _commands, _time);

    // Rows in the recorded statistics shape (start / end in epoch milliseconds), with the sum type.
    private sealed class FakeStatistics : IHomeAssistantCommands
    {
        public List<JsonObject> Requests { get; } = [];

        public Task<Result<JsonElement>> CommandAsync(string type, JsonObject fields, CancellationToken cancellationToken)
        {
            Assert.Equal("recorder/statistics_during_period", type);
            Requests.Add((JsonObject)fields.DeepClone());
            string json = (string?)fields["statistic_ids"]![0] == "sensor:dishwasher_cycles"
                ? """{"sensor:dishwasher_cycles":[{"start":1792908000000,"end":1792911600000,"sum":60.0},{"start":1792911600000,"end":1792915200000,"sum":61.0}]}"""
                : "{}";
            return Task.FromResult<Result<JsonElement>>(JsonDocument.Parse(json).RootElement.Clone());
        }
    }
}
