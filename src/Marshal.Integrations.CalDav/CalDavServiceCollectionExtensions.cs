using Marshal.Abstractions.Events;
using Marshal.Integrations.CalDav.Sync;
using Microsoft.Extensions.Hosting;
using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Health;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.CalDav;

/// <summary>
/// Registers CalDAV calendars (<c>calendar.caldav</c> in <c>marshal.yaml</c>). Without accounts nothing is polled and
/// the provider has no calendars.
/// </summary>
public static class CalDavServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalCalDav(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<CalDavOptions>().Bind(configuration.GetSection(CalDavOptions.Section)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<CalDavOptions>, CalDavOptionsValidator>());

        // GET, PUT (to a fixed resource, with If-Match) and the REPORTs are safe to repeat.
        services.AddHttpClient(HttpCalDavClient.HttpClientName).AddStandardResilienceHandler();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<ICalDavClient, HttpCalDavClient>();
        services.AddSingleton<CalDavCalendarProvider>();
        services.AddSingleton<ICalendarSource>(provider => provider.GetRequiredService<CalDavCalendarProvider>());
        services.AddSingleton<IHealthWarningProvider>(provider => provider.GetRequiredService<CalDavCalendarProvider>());

        services.TryAddSingleton<ICalDavSyncStore, EfCalDavSyncStore>();
        // The change poller (and its event source in the system view) only exists with accounts; demo mode simulates
        // the calendar events otherwise.
        if (configuration.GetSection(CalDavOptions.Section).GetSection("accounts").GetChildren().Any())
        {
            services.AddSingleton<CalDavSyncPoller>();
            services.AddSingleton<IEventSource>(provider => provider.GetRequiredService<CalDavSyncPoller>());
            services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<CalDavSyncPoller>());
        }
        return services;
    }
}
