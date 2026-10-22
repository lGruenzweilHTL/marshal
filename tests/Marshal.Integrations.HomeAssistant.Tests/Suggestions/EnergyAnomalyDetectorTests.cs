using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Suggestions;
using Marshal.Integrations.HomeAssistant.Suggestions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.HomeAssistant.Tests.Suggestions;

public sealed class EnergyAnomalyDetectorTests
{
    private static readonly OwnerId Owner = new(Guid.Parse("00000000-0000-7000-8000-00000000000a"));
    private static readonly TimeZoneInfo Vienna = TimeZoneInfo.FindSystemTimeZoneById("Europe/Vienna");

    // 2026-10-21 09:00 in Vienna; yesterday is 2026-10-20.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 21, 7, 0, 0, TimeSpan.Zero));
    private readonly FakeEnergy _commands = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DeviceAtFactorTimesMedian_Candidate()
    {
        _commands.Daily["sensor.dryer_energy"] = [.. Enumerable.Repeat(2.0, 14), 3.0];
        _commands.Daily["sensor.fridge_energy"] = [.. Enumerable.Repeat(1.0, 14), 1.2];

        SuggestionCandidate candidate = Assert.Single(await EvaluateAsync());

        Assert.Equal("sensor.dryer_energy:2026-10-20", candidate.DedupeKey);
        Assert.Equal("Dryer", candidate.Facts["device"]);
        Assert.Equal("3 kWh", candidate.Facts["yesterday"]);
        Assert.Equal("2 kWh", candidate.Facts["usual (median of the previous 14 days)"]);
        Assert.Equal("1.5", candidate.Facts["times the usual"]);
        JsonObject request = _commands.StatisticsRequest!;
        Assert.Equal("day", (string?)request["period"]);
        Assert.Equal("2026-10-05T22:00:00.0000000+00:00", (string?)request["start_time"]);
        Assert.Equal("2026-10-20T22:00:00.0000000+00:00", (string?)request["end_time"]);
        Assert.DoesNotContain("sensor.heat_pump_compressor_energy", request["statistic_ids"]!.AsArray().Select(id => (string?)id));
    }

    [Fact]
    public async Task MedianIgnoresOutliers()
    {
        _commands.Daily["sensor.oven_energy"] = [1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 30, 1.4];

        Assert.Empty(await EvaluateAsync());
    }

    [Fact]
    public async Task LessThanFourteenDays_NoCandidate()
    {
        _commands.Daily["sensor.dryer_energy"] = [.. Enumerable.Repeat(2.0, 13), 9.0];

        Assert.Empty(await EvaluateAsync());
    }

    [Fact]
    public async Task ConfiguredFactor_Applies()
    {
        _commands.Daily["sensor.dryer_energy"] = [.. Enumerable.Repeat(2.0, 14), 3.0];

        Assert.Empty(await EvaluateAsync(new SuggestionOptions { Detectors = { ["energy_anomaly"] = new SuggestionDetectorOptions { Factor = 2 } } }));
    }

    [Fact]
    public async Task NoDashboard_Nothing()
    {
        _commands.PrefsMissing = true;

        Assert.Empty(await EvaluateAsync());
        Assert.Null(_commands.StatisticsRequest);
    }

    private Task<IReadOnlyList<SuggestionCandidate>> EvaluateAsync(SuggestionOptions? options = null) =>
        new EnergyAnomalyDetector(new FixtureCache(), _commands, Options.Create(options ?? new SuggestionOptions()), _time)
            .EvaluateAsync(new SuggestionContext(Owner, "en", Vienna, null), Ct);

    // Recorded dashboard prefs; daily rows generated per device, oldest first, ending with yesterday.
    private sealed class FakeEnergy : IHomeAssistantCommands
    {
        public Dictionary<string, double[]> Daily { get; } = [];

        public bool PrefsMissing { get; set; }

        public JsonObject? StatisticsRequest { get; private set; }

        public Task<Result<JsonElement>> CommandAsync(string type, JsonObject fields, CancellationToken cancellationToken)
        {
            Result<JsonElement> result;
            if (type == "energy/get_prefs")
            {
                result = PrefsMissing
                    ? new Error("ha.command_failed", "No prefs", ErrorType.NotFound)
                    : JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "energy-prefs.json"))).RootElement.Clone();
            }
            else
            {
                Assert.Equal("recorder/statistics_during_period", type);
                StatisticsRequest = (JsonObject)fields.DeepClone();
                var end = DateTimeOffset.Parse((string)fields["end_time"]!, System.Globalization.CultureInfo.InvariantCulture);
                var response = new JsonObject();
                foreach ((string statistic, double[] values) in Daily)
                {
                    var rows = new JsonArray();
                    for (int index = 0; index < values.Length; index++)
                    {
                        DateTimeOffset start = end.AddDays(index - values.Length);
                        rows.Add(new JsonObject { ["start"] = start.ToUnixTimeMilliseconds(), ["end"] = start.AddDays(1).ToUnixTimeMilliseconds(), ["change"] = values[index] });
                    }

                    response[statistic] = rows;
                }

                result = JsonSerializer.SerializeToElement(response);
            }

            return Task.FromResult(result);
        }
    }
}
