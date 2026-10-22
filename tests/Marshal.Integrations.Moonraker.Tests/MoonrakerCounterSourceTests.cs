using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Maintenance;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class MoonrakerCounterSourceTests : IAsyncLifetime
{
    // The documented answer of GET /server/history/totals (Moonraker history API), inside Moonraker's result wrapper.
    private const string Totals = """
        {
          "job_totals": {
            "total_jobs": 3,
            "total_time": 11748.077333278954,
            "total_print_time": 1080000.0,
            "total_filament_used": 11615.718840001999,
            "longest_job": 11665.191012736992,
            "longest_print": 11348.794790096988
          },
          "auxiliary_totals": []
        }
        """;

    private FakeMoonraker _fake = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _fake = await FakeMoonraker.StartAsync();
        _fake.Rest["GET /server/history/totals"] = JsonNode.Parse(Totals);
    }

    public async ValueTask DisposeAsync() => await _fake.DisposeAsync();

    [Fact]
    public async Task PrintHours_SecondsAsHours()
    {
        Result<MaintenanceReading> reading = await Source().ReadAsync(new MaintenanceSource(Printer: "voron", Metric: MaintenanceMetric.PrintHours), Ct);

        Assert.Equal(new MaintenanceReading(300m, "h"), reading.Value);
        Assert.Equal(FakeMoonraker.ApiKey, Assert.Single(_fake.RestRequests).ApiKey);
    }

    [Fact]
    public async Task FilamentMeters_MillimetersAsMeters()
    {
        Result<MaintenanceReading> reading = await Source().ReadAsync(new MaintenanceSource(Printer: "voron", Metric: MaintenanceMetric.FilamentMeters), Ct);

        Assert.Equal("m", reading.Value.Unit);
        Assert.Equal(11.615718840001999m, reading.Value.Value);
    }

    [Fact]
    public async Task UnknownPrinter_AndHandles()
    {
        Assert.Equal("printer.not_found", (await Source().ReadAsync(new MaintenanceSource(Printer: "mini", Metric: MaintenanceMetric.PrintHours), Ct)).Error.Code);
        Assert.True(Source().Handles(new MaintenanceSource(Printer: "voron")));
        Assert.False(Source().Handles(new MaintenanceSource(Ha: "sensor.dishwasher_cycles")));
    }

    [Fact]
    public async Task ListCounters_PrintHoursPerPrinter()
    {
        IReadOnlyList<MaintenanceCounter> counters = await Source().ListCountersAsync(Ct);

        Assert.Equal(
            [new MaintenanceCounter(new MaintenanceSource(Printer: "voron", Metric: MaintenanceMetric.PrintHours), "voron", new MaintenanceReading(300m, "h"))],
            counters);
    }

    private MoonrakerCounterSource Source() => new(
        Microsoft.Extensions.Options.Options.Create(new PrinterOptions
        {
            Printers = [new PrinterConfig { Name = "voron", Url = _fake.Server.BaseAddress, ApiKey = FakeMoonraker.ApiKey }],
        }),
        new SingleClientFactory(_fake.Server.CreateClient()));
}
