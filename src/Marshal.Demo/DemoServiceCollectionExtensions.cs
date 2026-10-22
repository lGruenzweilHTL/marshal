using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Maintenance;
using Marshal.Abstractions.Mail;
using Marshal.Abstractions.Music;
using Marshal.Abstractions.Notes;
using Marshal.Abstractions.Printers;
using Marshal.Abstractions.Rss;
using Marshal.Abstractions.Tools;
using Marshal.Abstractions.Web;
using Marshal.Demo.Calendar;
using Marshal.Demo.HomeAssistant;
using Marshal.Demo.Mail;
using Marshal.Demo.Notes;
using Marshal.Demo.Printers;
using Marshal.Demo.Rss;
using Marshal.Demo.Seeding;
using Marshal.Demo.Web;
using Marshal.Integrations.HomeAssistant;
using Marshal.Integrations.HomeAssistant.Calendar;
using Marshal.Integrations.HomeAssistant.Energy;
using Marshal.Integrations.HomeAssistant.Music;
using Marshal.Integrations.Mail.Imap;
using Marshal.Integrations.Moonraker;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Marshal.Demo;

/// <summary>
/// Registers demo mode: with <c>demo.enabled</c>, every integration in scope that is not configured is replaced by
/// a simulated one behind the same interfaces, and the demo data is seeded. Call after every integration's own
/// registration; with demo mode off it only binds the options.
/// </summary>
public static class DemoServiceCollectionExtensions
{
    public const string Section = "demo";

    /// <summary>The URL simulated integrations report, so the real tool sources offer their tools; never contacted.</summary>
    internal static readonly Uri DemoUrl = new("http://demo.invalid/");

    /// <summary>
    /// Whether demo mode simulates the printer: demo mode on and no printer configured. The host then adds
    /// <see cref="DemoMaintenance"/> as a maintenance item.
    /// </summary>
    public static bool SimulatesPrinters(IConfiguration configuration) =>
        (configuration.GetSection(Section).Get<DemoOptions>() ?? new()).Enabled && !configuration.GetSection("printers").GetChildren().Any();

    public static IServiceCollection AddMarshalDemo(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<DemoOptions>().Bind(configuration.GetSection(Section));
        // Seeding and reset are always available, so marshal demo reset works after demo mode was turned off.
        services.AddSingleton(DemoSeedParts.All);
        services.AddSingleton<IDemoSeeder, DemoSeeder>();
        services.AddSingleton<IDemoReset, DemoReset>();

        DemoOptions options = configuration.GetSection(Section).Get<DemoOptions>() ?? new();
        if (!options.Enabled)
        {
            services.TryAddSingleton(new DemoIntegrations([]));
            return services;
        }

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(provider => new DemoWorld(provider.GetRequiredService<TimeProvider>()));
        var simulated = new List<DemoIntegration>();
        if (string.IsNullOrWhiteSpace(configuration["ha:url"]))
        {
            AddHomeAssistant(services, musicConfigured: configuration.GetSection("music:speakers").GetChildren().Any());
            simulated.Add(DemoIntegration.HomeAssistant);
        }

        if (!configuration.GetSection("calendar:caldav:accounts").GetChildren().Any())
        {
            AddCalDav(services);
            simulated.Add(DemoIntegration.Caldav);
        }

        if (SimulatesPrinters(configuration))
        {
            AddPrinters(services);
            simulated.Add(DemoIntegration.Printers);
        }

        // Mail accounts are database rows, so mail is never "configured" at startup: the demo account shows while the
        // owner has no account of their own.
        if (services.Any(descriptor => descriptor.ServiceType == typeof(IMailAccountStore)))
        {
            AddMail(services);
            simulated.Add(DemoIntegration.Mail);
        }

        // FreshRSS and notes are "configured" when their real client says so (secrets and the notes directory are only
        // known at runtime), so the demo wraps them and steps aside while the real one is configured.
        bool rss = services.Any(descriptor => descriptor.ServiceType == typeof(IRssClient));
        if (rss)
            AddRss(services);

        bool notes = services.Any(descriptor => descriptor.ServiceType == typeof(INotesSource));
        if (notes)
            services.Decorate<INotesSource>((provider, inner) => new DemoNotesSource(inner, provider.GetRequiredService<DemoWorld>()));

        bool web = services.Any(descriptor => descriptor.ServiceType == typeof(IWebSearchProvider));
        if (web)
            services.Decorate<IWebSearchProvider>((provider, inner) => new DemoWebSearch(inner));

        services.AddHostedService<DemoSeedService>();

        services.AddSingleton(provider => new DemoIntegrations(
        [
            .. simulated,
            .. rss && provider.GetRequiredService<IRssClient>() is DemoRssClient { IsReal: false } ? [DemoIntegration.Rss] : Array.Empty<DemoIntegration>(),
            .. notes && provider.GetRequiredService<INotesSource>() is DemoNotesSource { IsReal: false } ? [DemoIntegration.Notes] : Array.Empty<DemoIntegration>(),
            .. web && provider.GetRequiredService<IWebSearchProvider>() is DemoWebSearch { IsReal: false } ? [DemoIntegration.Web] : Array.Empty<DemoIntegration>(),
        ]));
        return services;
    }

    private static void AddHomeAssistant(IServiceCollection services, bool musicConfigured)
    {
        System.Reflection.Assembly ha = typeof(HomeAssistantOptions).Assembly;
        IOptions<HomeAssistantOptions> demoOptions = Options.Create(new HomeAssistantOptions { Url = DemoUrl });

        services.Replace(ServiceDescriptor.Singleton<IHomeAssistantClient, DemoHomeAssistantClient>());
        services.Replace(ServiceDescriptor.Singleton<IHomeAssistantCache, DemoHomeAssistantCache>());
        services.Replace(ServiceDescriptor.Singleton<IHomeAssistantCommands, DemoHomeAssistantCommands>());
        // Only the HA calendars are simulated; CalDAV accounts stay as configured.
        services.RemoveImplementation(typeof(ICalendarSource), typeof(HomeAssistantCalendarProvider));
        services.AddSingleton<ICalendarSource, DemoCalendarProvider>();
        services.Replace(ServiceDescriptor.Singleton<IMusicPlayer, DemoMusicPlayer>());
        if (!musicConfigured)
            services.Replace(ServiceDescriptor.Singleton<ISpeakerDirectory, DemoSpeakerDirectory>());

        // The real cache and event source stay inert without ha.url; the demo ones take their place.
        services.RemoveFrom(typeof(IEventSource), ha).RemoveFrom(typeof(IHostedService), ha);
        services.AddSingleton<DemoHomeAssistantEventSource>();
        services.AddSingleton<IEventSource>(provider => provider.GetRequiredService<DemoHomeAssistantEventSource>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<DemoHomeAssistantEventSource>());

        // The real tool sources, built with options that name the (never contacted) demo URL.
        foreach (Type source in new[] { typeof(TimerToolSource), typeof(PlacesToolSource), typeof(EnergyToolSource), typeof(MusicToolSource) })
        {
            services.RemoveImplementation(typeof(IToolSource), source);
            services.AddSingleton(typeof(IToolSource), provider => ActivatorUtilities.CreateInstance(provider, source, demoOptions));
        }

        services.AddSingleton<IToolSource, DemoHaToolSource>();
    }

    // Next to the real CalDAV provider, which stays unconfigured and so out of the composite's routing.
    private static void AddCalDav(IServiceCollection services)
    {
        services.AddSingleton<DemoCalDavCalendars>();
        services.AddSingleton<ICalendarSource>(provider => provider.GetRequiredService<DemoCalDavCalendars>());
        services.AddSingleton<DemoCalDavEventSource>();
        services.AddSingleton<IEventSource>(provider => provider.GetRequiredService<DemoCalDavEventSource>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<DemoCalDavEventSource>());
    }

    private static void AddMail(IServiceCollection services)
    {
        services.Decorate<IMailProvider>((provider, inner) => new DemoMailProvider(inner, provider.GetRequiredService<DemoWorld>()));
        object realAccounts = services.Decorate<IMailAccountStore>((provider, inner) => ActivatorUtilities.CreateInstance<DemoMailAccountStore>(provider, inner));

        // The new-mail watcher connects to every account it lists, so it gets the real store without the demo account.
        services.Replace(ServiceDescriptor.Singleton(provider => ActivatorUtilities.CreateInstance<ImapNewMailSource>(provider, provider.GetRequiredKeyedService<IMailAccountStore>(realAccounts))));
    }

    private static void AddRss(IServiceCollection services)
    {
        services.Decorate<IRssClient>((provider, inner) => new DemoRssClient(inner, provider.GetRequiredService<DemoWorld>()));
        services.Decorate<IFullTextExtractor>((provider, inner) => new DemoFullTextExtractor(inner, provider.GetRequiredService<IRssClient>(), provider.GetRequiredService<DemoWorld>()));
    }

    private static void AddPrinters(IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Singleton<IPrinterDirectory, DemoPrinterDirectory>());
        services.Replace(ServiceDescriptor.Singleton<IPrinterClient, DemoPrinterClient>());
        services.Replace(ServiceDescriptor.Singleton<IPrinterWebcams, DemoPrinterWebcams>());
        services.Replace(ServiceDescriptor.Singleton<ISnapshotSource, DemoSnapshotSource>());

        // The Moonraker cache stays registered but has no printers to connect to. The printer event source, the
        // babysitter and the tools read the demo cache through the interface.
        services.AddSingleton<DemoPrinterStateCache>();
        services.Replace(ServiceDescriptor.Singleton<IPrinterStateCache>(provider => provider.GetRequiredService<DemoPrinterStateCache>()));
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<DemoPrinterStateCache>());

        services.RemoveImplementation(typeof(IMaintenanceCounterSource), typeof(MoonrakerCounterSource));
        services.AddSingleton<IMaintenanceCounterSource, DemoPrinterCounterSource>();
        // The seeded history starts the demo maintenance item, which the host adds with the simulated printer.
        services.Replace(ServiceDescriptor.Singleton(DemoSeedParts.All with { MaintenanceItem = true }));
    }
}
