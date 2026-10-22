using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Suggestions;
using Marshal.Integrations.HomeAssistant.Suggestions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.HomeAssistant.Tests.Suggestions;

public sealed class WindowRainDetectorTests
{
    private static readonly OwnerId Owner = new(Guid.Parse("00000000-0000-7000-8000-00000000000a"));

    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 8, 20, 0, TimeSpan.Zero));
    private readonly FakeForecasts _commands = new();
    private readonly FixtureCache _cache = new();
    private SuggestionOptions _options = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task OpenWindow_RainWithinTheHour_CandidateNamingTheWindow()
    {
        WithWindow("on");

        SuggestionCandidate candidate = Assert.Single(await EvaluateAsync());

        Assert.Equal("binary_sensor.bedroom_window:2026-10-21", candidate.DedupeKey);
        Assert.Equal("Bedroom window", candidate.Facts["opening"]);
        Assert.Equal("Bedroom", candidate.Facts["area"]);
        Assert.Equal("80%", candidate.Facts["precipitation probability"]);
        Assert.Equal("within the next hour", candidate.Facts["forecast"]);
        Assert.Null(candidate.Action);
        JsonObject call = Assert.Single(_commands.Calls);
        Assert.Equal(("weather", "get_forecasts", "hourly", "weather.demo_weather_north", true), (
            (string?)call["domain"], (string?)call["service"], (string?)call["service_data"]!["type"], (string?)call["target"]!["entity_id"], (bool?)call["return_response"]));
    }

    [Fact]
    public async Task ClosedWindow_NoCandidate_ForecastNotRead()
    {
        WithWindow("off");

        Assert.Empty(await EvaluateAsync());
        Assert.Empty(_commands.Calls);
    }

    [Fact]
    public async Task LowProbability_NoCandidate()
    {
        WithWindow("on");
        _options = new SuggestionOptions { Detectors = { ["window_rain"] = new SuggestionDetectorOptions { MinProbability = 85 } } };

        Assert.Empty(await EvaluateAsync());
    }

    [Fact]
    public async Task NoHourlyForecast_FallsBackToDaily()
    {
        WithWindow("on");
        _commands.HourlyFails = true;

        SuggestionCandidate candidate = Assert.Single(await EvaluateAsync());

        Assert.Equal("70%", candidate.Facts["precipitation probability"]);
        Assert.Equal("today", candidate.Facts["forecast"]);
        Assert.Equal(["hourly", "daily"], _commands.Calls.Select(call => (string?)call["service_data"]!["type"]));
    }

    [Fact]
    public async Task NoForecastAtAll_Silent()
    {
        WithWindow("on");
        _commands.HourlyFails = true;
        _commands.DailyFails = true;

        Assert.Empty(await EvaluateAsync());
    }

    [Fact]
    public async Task StateChangeOfOtherEntity_SkipsWithoutReading()
    {
        WithWindow("on");
        var @event = new MarshalEvent("ha", "state_changed", "light.kitchen", JsonSerializer.SerializeToElement(new { from = "off", to = "on" }), null, _time.GetUtcNow());

        Assert.Empty(await EvaluateAsync(@event));
        Assert.Empty(_commands.Calls);
    }

    [Fact]
    public async Task StillHolds_WhileOpen()
    {
        WithWindow("on");
        Assert.True(await Detector().StillHoldsAsync(Owner, "binary_sensor.bedroom_window:2026-10-21", Ct));

        WithWindow("off");
        Assert.False(await Detector().StillHoldsAsync(Owner, "binary_sensor.bedroom_window:2026-10-21", Ct));
    }

    private Task<IReadOnlyList<SuggestionCandidate>> EvaluateAsync(MarshalEvent? @event = null) =>
        Detector().EvaluateAsync(new SuggestionContext(Owner, "en", TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna"), @event), Ct);

    private WindowRainDetector Detector() => new(_cache, _commands, Options.Create(_options), _time);

    private void WithWindow(string state)
    {
        HaSnapshot recorded = FixtureCache.Recorded;
        const string Id = "binary_sensor.bedroom_window";
        _cache.Current = recorded with
        {
            Entities = recorded.Entities.SetItem(Id, new HaEntity(Id, "Bedroom window", [], "bedroom", null, "window", false)),
            States = recorded.States.SetItem(Id, new HaState(Id, state, JsonSerializer.SerializeToElement(new { friendly_name = "Bedroom window", device_class = "window" }), _time.GetUtcNow())),
        };
    }

    private sealed class FakeForecasts : IHomeAssistantCommands
    {
        public bool HourlyFails { get; set; }

        public bool DailyFails { get; set; }

        public List<JsonObject> Calls { get; } = [];

        public Task<Result<JsonElement>> CommandAsync(string type, JsonObject fields, CancellationToken cancellationToken)
        {
            Assert.Equal("call_service", type);
            Calls.Add((JsonObject)fields.DeepClone());
            string forecast = (string)fields["service_data"]!["type"]!;
            bool fails = forecast == "hourly" ? HourlyFails : DailyFails;
            Result<JsonElement> result = fails
                ? new Error("ha.command_failed", $"Weather entity does not support '{forecast}' forecast", ErrorType.Failure)
                : JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", $"weather-forecast-{forecast}.json"))).RootElement.Clone();
            return Task.FromResult(result);
        }
    }
}
