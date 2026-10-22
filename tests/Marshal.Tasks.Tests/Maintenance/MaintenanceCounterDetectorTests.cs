using Marshal.Abstractions;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Suggestions;
using Marshal.Tasks.Maintenance;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Tests.Maintenance;

public sealed class MaintenanceCounterDetectorTests
{
    private static readonly OwnerId _owner = new(Guid.Parse("00000000-0000-0000-0000-000000000001"));

    private readonly MaintenanceOptions _options = new();
    private readonly ListedCounters _printers = new(
        new MaintenanceCounter(new MaintenanceSource(Printer: "voron", Metric: MaintenanceMetric.PrintHours), "voron", new MaintenanceReading(1234.56m, "h")));
    private readonly ListedCounters _ha = new(
        new MaintenanceCounter(new MaintenanceSource(Ha: "sensor.dishwasher_cycles"), "Dishwasher cycles", new MaintenanceReading(61m, null)),
        new MaintenanceCounter(new MaintenanceSource(Ha: "sensor.heat_pump_hours"), "Heat pump \"main\" hours", null));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PrinterWithoutItem_SuggestsNozzleItemWithYamlSnippet()
    {
        IReadOnlyList<SuggestionCandidate> candidates = await Detector().EvaluateAsync(Context("en"), Ct);

        SuggestionCandidate printer = Assert.Single(candidates, candidate => candidate.DedupeKey == "printer:voron");
        Assert.Equal("voron", printer.Facts["counter"]);
        Assert.Equal("1234.6 h", printer.Facts["current value"]);
        Assert.Equal("Voron nozzle", printer.Facts["suggested item"]);
        Assert.Null(printer.Action);
        Assert.Equal(
            """
            maintenance:
              items:
                voron_nozzle:
                  name: "Voron nozzle"
                  source: { printer: voron, metric: printHours }
                  every: 300
                  warnBefore: 20

            """.ReplaceLineEndings("\n"),
            printer.Snippet);
    }

    [Fact]
    public async Task HaCounters_SuggestItemsWithTheirEntity()
    {
        IReadOnlyList<SuggestionCandidate> candidates = await Detector().EvaluateAsync(Context("de"), Ct);

        Assert.Equal(["printer:voron", "ha:sensor.dishwasher_cycles", "ha:sensor.heat_pump_hours"], candidates.Select(candidate => candidate.DedupeKey));
        Assert.Equal("Voron Düse", candidates[0].Facts["suggested item"]);
        SuggestionCandidate dishwasher = candidates[1];
        Assert.Equal("61", dishwasher.Facts["current value"]);
        Assert.Contains("    dishwasher_cycles:\n      name: \"Dishwasher cycles\"\n      source: { ha: sensor.dishwasher_cycles }\n", dishwasher.Snippet);
        Assert.False(candidates[2].Facts.ContainsKey("current value"));
        Assert.Contains("name: \"Heat pump \\\"main\\\" hours\"", candidates[2].Snippet);
    }

    [Fact]
    public async Task CountersWithConfiguredItems_GiveNothing()
    {
        _options.Items["nozzle"] = new() { Name = "Nozzle", Source = new() { Printer = "voron", Metric = MaintenanceMetric.FilamentMeters }, Every = 1000 };
        _options.Items["dishwasher_filter"] = new() { Name = "Filter", Source = new() { Ha = "sensor.dishwasher_cycles" }, Every = 60 };
        _options.Items["pump"] = new() { Name = "Pump", Source = new() { Ha = "sensor.heat_pump_hours" }, Every = 1000 };

        Assert.Empty(await Detector().EvaluateAsync(Context("en"), Ct));
        Assert.False(await Detector().StillHoldsAsync(_owner, "printer:voron", Ct));
    }

    [Fact]
    public async Task OpenSuggestion_HoldsUntilAnItemUsesTheCounter()
    {
        Assert.True(await Detector().StillHoldsAsync(_owner, "ha:sensor.dishwasher_cycles", Ct));

        _options.Items["dishwasher_filter"] = new() { Name = "Filter", Source = new() { Ha = "sensor.dishwasher_cycles" }, Every = 60 };

        Assert.False(await Detector().StillHoldsAsync(_owner, "ha:sensor.dishwasher_cycles", Ct));
        Assert.Equal(TimeSpan.FromDays(1), Detector().Triggers.Interval);
        Assert.Empty(Detector().Triggers.Events);
    }

    private MaintenanceCounterDetector Detector() => new(Options.Create(_options), [_printers, _ha]);

    private static SuggestionContext Context(string language) => new(_owner, language, TimeZoneInfo.Utc, null);

    private sealed class ListedCounters(params MaintenanceCounter[] counters) : IMaintenanceCounterSource
    {
        public bool Handles(MaintenanceSource source) => false;

        public Task<Result<MaintenanceReading>> ReadAsync(MaintenanceSource source, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<MaintenanceCounter>> ListCountersAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<MaintenanceCounter>>(counters);
    }
}
