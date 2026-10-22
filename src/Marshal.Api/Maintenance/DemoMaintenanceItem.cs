using Marshal.Abstractions.Maintenance;
using Marshal.Demo;
using Marshal.Tasks.Maintenance;
using Microsoft.Extensions.Options;

namespace Marshal.Api.Maintenance;

/// <summary>
/// In demo mode with the simulated printer and no configured items, adds <see cref="DemoMaintenance"/> to
/// <c>maintenance.items</c>. Decided when the options are first read, from the host's full configuration.
/// </summary>
internal static class DemoMaintenanceItem
{
    public static IServiceCollection AddDemoMaintenanceItem(this IServiceCollection services) =>
        services.AddSingleton<IPostConfigureOptions<MaintenanceOptions>>(provider => new PostConfigureOptions<MaintenanceOptions>(Options.DefaultName, options =>
        {
            if (options.Items.Count > 0 || !DemoServiceCollectionExtensions.SimulatesPrinters(provider.GetRequiredService<IConfiguration>()))
                return;

            options.Items[DemoMaintenance.ItemId] = new MaintenanceItemOptions
            {
                Name = DemoMaintenance.Name,
                Source = new MaintenanceSourceOptions { Printer = DemoPrinter.Name, Metric = MaintenanceMetric.PrintHours },
                Every = DemoMaintenance.Every,
                WarnBefore = DemoMaintenance.WarnBefore,
            };
        }));
}
