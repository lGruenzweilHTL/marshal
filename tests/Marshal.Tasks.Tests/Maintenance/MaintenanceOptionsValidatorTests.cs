using Marshal.Abstractions;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Printers;
using Marshal.Tasks.Maintenance;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Tests.Maintenance;

public sealed class MaintenanceOptionsValidatorTests
{
    [Fact]
    public void BothSourceKinds_FromConfiguration_Valid()
    {
        MaintenanceOptions options = Bind(new Dictionary<string, string?>
        {
            ["maintenance:items:voron_nozzle:name"] = "Voron nozzle",
            ["maintenance:items:voron_nozzle:source:printer"] = "voron",
            ["maintenance:items:voron_nozzle:source:metric"] = "printHours",
            ["maintenance:items:voron_nozzle:every"] = "300",
            ["maintenance:items:voron_nozzle:warnBefore"] = "20",
            ["maintenance:items:dishwasher_filter:name"] = "Dishwasher filter",
            ["maintenance:items:dishwasher_filter:source:ha"] = "sensor.dishwasher_cycles",
            ["maintenance:items:dishwasher_filter:every"] = "60",
        });

        Assert.True(Validate(options).Succeeded);
        Assert.Equal(new MaintenanceSource(Printer: "voron", Metric: MaintenanceMetric.PrintHours), options.Items["voron_nozzle"].Source.ToSource());
        Assert.Equal((300m, 20m), (options.Items["voron_nozzle"].Every, options.Items["voron_nozzle"].WarnBefore));
        Assert.Equal("sensor.dishwasher_cycles", options.Items["dishwasher_filter"].Source.ToSource().Ha);
    }

    [Theory]
    [InlineData("Voron-Nozzle", "voron", null, 300, null, "maintenance.items.Voron-Nozzle")]
    [InlineData("nozzle", "mini", null, 300, null, "maintenance.items.nozzle.source.printer")]
    [InlineData("nozzle", "voron", "sensor.x", 300, null, "maintenance.items.nozzle.source")]
    [InlineData("nozzle", null, null, 300, null, "maintenance.items.nozzle.source")]
    [InlineData("nozzle", "voron", null, 0, null, "maintenance.items.nozzle.every")]
    [InlineData("nozzle", "voron", null, 300, 300, "maintenance.items.nozzle.warnBefore")]
    [InlineData("nozzle", "voron", null, 300, -1, "maintenance.items.nozzle.warnBefore")]
    public void Invalid_FailsNamingTheKey(string id, string? printer, string? ha, int every, int? warnBefore, string key)
    {
        var options = new MaintenanceOptions
        {
            Items =
            {
                [id] = new MaintenanceItemOptions
                {
                    Name = "Nozzle",
                    Source = new MaintenanceSourceOptions { Printer = printer, Ha = ha, Metric = printer is null ? null : MaintenanceMetric.PrintHours },
                    Every = every,
                    WarnBefore = warnBefore,
                },
            },
        };

        ValidateOptionsResult result = Validate(options);

        Assert.True(result.Failed);
        Assert.StartsWith(key, result.FailureMessage);
    }

    [Fact]
    public void PrinterWithoutMetric_Fails()
    {
        var options = new MaintenanceOptions { Items = { ["nozzle"] = new() { Name = "Nozzle", Source = new() { Printer = "voron" }, Every = 300 } } };

        Assert.StartsWith("maintenance.items.nozzle.source.metric", Validate(options).FailureMessage);
    }

    private static ValidateOptionsResult Validate(MaintenanceOptions options) => new MaintenanceOptionsValidator([new OnePrinter()]).Validate(null, options);

    private static MaintenanceOptions Bind(Dictionary<string, string?> values)
    {
        var options = new MaintenanceOptions();
        new ConfigurationBuilder().AddInMemoryCollection(values).Build().GetSection("maintenance").Bind(options);
        return options;
    }

    private sealed class OnePrinter : IPrinterDirectory
    {
        public IReadOnlyList<PrinterInfo> Printers { get; } = [new PrinterInfo("voron", [])];

        public Result<PrinterInfo> Resolve(string? printer) => throw new NotSupportedException();
    }
}
