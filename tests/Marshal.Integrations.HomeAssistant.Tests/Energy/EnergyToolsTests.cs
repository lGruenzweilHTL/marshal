using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;
using Marshal.Integrations.HomeAssistant.Energy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Integrations.HomeAssistant.Tests.Energy;

public sealed class EnergyToolsTests
{
    // Thursday 2026-10-08 14:37 in Vienna.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 12, 37, 0, TimeSpan.Zero));
    private readonly FakeEnergyCommands _commands = new();
    private readonly FixtureCache _cache = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Usage_Week_TotalsCostsUtilitiesAndTopDevices()
    {
        JsonElement usage = await UsageAsync(new { period = "week" });

        Assert.Equal(("2026-10-05T00:00", "2026-10-08T14:00"), (usage.GetProperty("period").GetProperty("from").GetString(), usage.GetProperty("period").GetProperty("to").GetString()));
        Assert.Equal(("kWh", "EUR"), (usage.GetProperty("unit").GetString(), usage.GetProperty("currency").GetString()));
        Assert.Equal(
            (41.2, 3.1, 18.4, 4.0, 3.6, 56.1, 12.4, 0.25),
            (Number(usage, "gridImport"), Number(usage, "gridExport"), Number(usage, "solar"), Number(usage, "batteryCharge"), Number(usage, "batteryDischarge"),
                Number(usage, "consumption"), Number(usage, "cost"), Number(usage, "compensation")));
        JsonElement gas = usage.GetProperty("gas");
        Assert.Equal((12.3, "m³", 9.8), (Number(gas, "value"), gas.GetProperty("unit").GetString(), Number(gas, "cost")));
        JsonElement water = usage.GetProperty("water");
        Assert.Equal((2.1, "m³"), (Number(water, "value"), water.GetProperty("unit").GetString()));
        Assert.False(water.TryGetProperty("cost", out _));
        Assert.Equal(
            [("Heat pump", 22.0), ("Dryer", 4.5), ("sensor.washing_machine_energy", 3.2), ("Oven", 2.8), ("Dishwasher", 2.4)],
            usage.GetProperty("topDevices").EnumerateArray().Select(device => (device.GetProperty("name").GetString(), Number(device, "value"))));
        Assert.False(usage.TryGetProperty("previous", out _));
    }

    [Fact]
    public async Task Usage_LegacyGridShape_SameResult()
    {
        string current = (await InvokeAsync(new { period = "week" })).Value.Content;
        _commands.Prefs = Fixture("energy-prefs-legacy.json");

        Assert.Equal(current, (await InvokeAsync(new { period = "week" })).Value.Content);
    }

    [Fact]
    public async Task Usage_OneStatisticsRequestWithHourlyChangeInKwhAndCubicMeters()
    {
        await UsageAsync(new { period = "week" });

        JsonObject request = Assert.Single(_commands.StatisticsRequests);
        Assert.Equal(("2026-10-04T22:00:00.0000000+00:00", "2026-10-08T12:00:00.0000000+00:00", "hour"), ((string?)request["start_time"], (string?)request["end_time"], (string?)request["period"]));
        Assert.Equal(["change"], request["types"]!.AsArray().Select(type => (string?)type));
        Assert.Equal(("kWh", "m³"), ((string?)request["units"]!["energy"], (string?)request["units"]!["volume"]));
        string[] ids = [.. request["statistic_ids"]!.AsArray().Select(id => (string)id!)];
        Assert.Contains("sensor.grid_import_cost", ids);
        Assert.Contains("sensor.gas_meter_cost", ids);
        Assert.DoesNotContain("sensor.heat_pump_compressor_energy", ids);
    }

    [Fact]
    public async Task Usage_ConsumptionWithSolar()
    {
        _commands.Prefs = Prefs("""{"type":"grid","stat_energy_from":"sensor.grid_import","stat_energy_to":"sensor.grid_export"}""", """{"type":"solar","stat_energy_from":"sensor.solar_production"}""");
        _commands.Statistics = Rows(("sensor.grid_import", 8), ("sensor.grid_export", 5), ("sensor.solar_production", 12));

        JsonElement usage = await UsageAsync(new { period = "today" });

        Assert.Equal(15.0, Number(usage, "consumption"));
        Assert.False(usage.TryGetProperty("batteryCharge", out _));
    }

    [Fact]
    public async Task Usage_NoPriceAndNoCostStatistic_NoCostField()
    {
        _commands.Prefs = Prefs("""{"type":"grid","stat_energy_from":"sensor.grid_import","stat_cost":null}""");
        _commands.Info = JsonSerializer.SerializeToElement(new { cost_sensors = new { } });
        _commands.Statistics = Rows(("sensor.grid_import", 8));

        JsonElement usage = await UsageAsync(new { period = "yesterday" });

        Assert.Equal(8.0, Number(usage, "gridImport"));
        Assert.False(usage.TryGetProperty("cost", out _));
    }

    [Fact]
    public async Task Usage_AbsentSourcesOmitted()
    {
        _commands.Prefs = Prefs("""{"type":"grid","stat_energy_from":"sensor.grid_import"}""");
        _commands.Statistics = Rows(("sensor.grid_import", 8));

        JsonElement usage = await UsageAsync(new { period = "today" });

        foreach (string absent in new[] { "gridExport", "solar", "batteryCharge", "batteryDischarge", "gas", "water", "topDevices", "compensation" })
            Assert.False(usage.TryGetProperty(absent, out _), absent);
    }

    [Fact]
    public async Task Usage_ComparePrevious_AddsPreviousWithASecondRequest()
    {
        JsonElement usage = await UsageAsync(new { period = "week", compareWith = "previous" });

        JsonElement previous = usage.GetProperty("previous");
        Assert.Equal(("2026-09-28T00:00", "2026-10-01T14:00"), (previous.GetProperty("period").GetProperty("from").GetString(), previous.GetProperty("period").GetProperty("to").GetString()));
        Assert.Equal(41.2, Number(previous, "gridImport"));
        Assert.Equal(2, _commands.StatisticsRequests.Count);
        Assert.Equal("2026-09-27T22:00:00.0000000+00:00", (string?)_commands.StatisticsRequests[1]["start_time"]);
    }

    [Fact]
    public async Task Usage_LongRange_DailyStatistics()
    {
        JsonElement usage = await UsageAsync(new { period = "range", from = "2026-01-01", to = "2026-06-30" });

        Assert.Equal("day", (string?)Assert.Single(_commands.StatisticsRequests)["period"]);
        Assert.Equal("day", usage.GetProperty("period").GetProperty("resolution").GetString());
    }

    [Theory]
    [InlineData("""{"period":"fortnight"}""")]
    [InlineData("""{"period":"range","from":"2026-10-01"}""")]
    [InlineData("""{"period":"range","from":"2026-10-08","to":"2026-10-01"}""")]
    [InlineData("""{"period":"week","compareWith":"last_year"}""")]
    public async Task Usage_InvalidArguments_ValidationErrorWithoutRequests(string arguments)
    {
        Result<ToolOutput> usage = await (await ToolAsync()).InvokeAsync(new ToolInvocation(JsonDocument.Parse(arguments).RootElement, Text(OwnerA)), Ct);

        Assert.Equal(ErrorType.Validation, usage.Error.Type);
        Assert.Empty(_commands.Sent);
    }

    [Fact]
    public async Task Usage_Disconnected_Unavailable()
    {
        _commands.Connected = false;

        Assert.Equal("ha.unavailable", (await InvokeAsync(new { period = "today" })).Error.Code);
    }

    [Fact]
    public async Task Visibility_NoToolWithoutHomeAssistant()
    {
        var source = new EnergyToolSource(_cache, _commands, new ViennaProfiles(), Options.Create(new HomeAssistantOptions()), _time);

        Assert.Empty(await source.GetToolsAsync(Ct));
    }

    [Fact]
    public async Task Visibility_HiddenWithoutSources()
    {
        _commands.Prefs = Fixture("energy-prefs-empty.json");

        Assert.False(await (await ToolAsync()).IsAvailableAsync(Text(OwnerA), Ct));
    }

    [Fact]
    public async Task Visibility_HiddenWhenTheDashboardWasNeverSetUp()
    {
        _commands.PrefsError = new Error("ha.command_failed", "No prefs", ErrorType.Unavailable);

        Assert.False(await (await ToolAsync()).IsAvailableAsync(Text(OwnerA), Ct));
    }

    [Fact]
    public async Task Visibility_PrefsReadOncePerWindowAndKeptWhileDisconnected()
    {
        ITool tool = await ToolAsync();

        for (int i = 0; i < 3; i++)
            Assert.True(await tool.IsAvailableAsync(Text(OwnerA), Ct));
        Assert.Equal(1, _commands.PrefsReads);

        _time.Advance(EnergyTools.PrefsLifetime);
        _commands.Connected = false;
        Assert.True(await tool.IsAvailableAsync(Text(OwnerA), Ct));
        _commands.Connected = true;
        _commands.Prefs = Fixture("energy-prefs-empty.json");
        Assert.False(await tool.IsAvailableAsync(Text(OwnerA), Ct));
        Assert.Equal(3, _commands.PrefsReads);
    }

    [Fact]
    public async Task Registry_WithAFakeHomeAssistantThatHasEnergySources_EnergyUsageVisible()
    {
        await using FakeHomeAssistant ha = await FakeHomeAssistant.StartAsync();
        ha.Responses["energy/get_prefs"] = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "energy-prefs.json")));
        IOptions<HomeAssistantOptions> options = Options.Create(new HomeAssistantOptions { Url = new Uri("http://homeassistant.local:8123"), Token = FakeHomeAssistant.Token });
        using HomeAssistantCache cache = new(options, new TestServerSockets(ha.Server), new HomeAssistantCacheTimings { CommandTimeout = TimeSpan.FromSeconds(2) }, TimeProvider.System, NullLogger<HomeAssistantCache>.Instance);
        await cache.StartAsync(Ct);
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (cache.Current is null && DateTime.UtcNow < deadline)
                await Task.Delay(10, Ct);
            ToolRegistry registry = new([new EnergyToolSource(cache, cache, new ViennaProfiles(), options, TimeProvider.System)]);

            await registry.RefreshAsync(Ct);

            RegisteredTool usage = Assert.Single(registry.Tools);
            Assert.Equal("energy_usage", usage.ModelName);
            Assert.True(await usage.Tool.IsAvailableAsync(Text(OwnerA), Ct));
        }
        finally
        {
            await cache.StopAsync(CancellationToken.None);
        }
    }

    private async Task<JsonElement> UsageAsync(object arguments)
    {
        Result<ToolOutput> usage = await InvokeAsync(arguments);
        Assert.True(usage.IsSuccess, usage.IsFailure ? usage.Error.Message : null);
        return JsonDocument.Parse(usage.Value.Content).RootElement.Clone();
    }

    private async Task<Result<ToolOutput>> InvokeAsync(object arguments) =>
        await (await ToolAsync()).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), Text(OwnerA)), Ct);

    private async Task<ITool> ToolAsync() =>
        Assert.Single(await new EnergyToolSource(_cache, _commands, new ViennaProfiles(), Options.Create(new HomeAssistantOptions { Url = new Uri("http://ha.local/") }), _time).GetToolsAsync(Ct));

    private static double Number(JsonElement element, string property) => element.GetProperty(property).GetDouble();

    private static JsonElement Fixture(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file))).RootElement.Clone();

    private static JsonElement Prefs(params string[] sources) =>
        JsonDocument.Parse($$"""{"energy_sources":[{{string.Join(",", sources)}}],"device_consumption":[]}""").RootElement.Clone();

    private static JsonElement Rows(params (string Statistic, double Change)[] totals) =>
        JsonSerializer.SerializeToElement(totals.ToDictionary(total => total.Statistic, total => new[] { new { start = 0L, end = 3_600_000L, change = total.Change } }));

    private sealed class ViennaProfiles : IUserProfileProvider
    {
        public Task<Result<UserProfile>> GetAsync(OwnerId owner, CancellationToken cancellationToken) =>
            Task.FromResult<Result<UserProfile>>(new UserProfile(owner, "Alice", "de", "Europe/Vienna"));
    }

    private sealed class FakeEnergyCommands : IHomeAssistantCommands
    {
        public JsonElement Prefs { get; set; } = Fixture("energy-prefs.json");

        public Error? PrefsError { get; set; }

        public JsonElement Info { get; set; } = Fixture("energy-info.json");

        public JsonElement Statistics { get; set; } = Fixture("energy-statistics.json");

        public bool Connected { get; set; } = true;

        public List<string> Sent { get; } = [];

        public List<JsonObject> StatisticsRequests { get; } = [];

        public int PrefsReads => Sent.Count(type => type == "energy/get_prefs");

        public Task<Result<JsonElement>> CommandAsync(string type, JsonObject fields, CancellationToken cancellationToken)
        {
            Sent.Add(type);
            if (!Connected)
                return Task.FromResult<Result<JsonElement>>(new Error("ha.unavailable", "Home Assistant is not connected.", ErrorType.Unavailable));

            Result<JsonElement> result = type switch
            {
                "energy/get_prefs" => PrefsError is { } error ? error : Prefs,
                "energy/info" => Info,
                "recorder/statistics_during_period" => Record(fields),
                _ => new Error("ha.command_failed", $"Unknown command {type}", ErrorType.Unavailable),
            };
            return Task.FromResult(result);
        }

        private JsonElement Record(JsonObject fields)
        {
            StatisticsRequests.Add((JsonObject)fields.DeepClone());
            return Statistics;
        }
    }
}
