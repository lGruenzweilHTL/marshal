using Marshal.Abstractions.Calendar;
using Marshal.Abstractions.Context;
using Marshal.Abstractions.Displays;
using Marshal.Abstractions.Events;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Satellites;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.HomeAssistant.Calendar;
using Marshal.Integrations.HomeAssistant.Displays;
using Marshal.Integrations.HomeAssistant.Energy;
using Marshal.Integrations.HomeAssistant.Music;
using Marshal.Integrations.HomeAssistant.Notifications;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Registers the Home Assistant integration. Without <c>ha.url</c> every part stays inert (no connection, no timer
/// tools, no context, ids as area names, time zone from config or UTC). Call after the core services: it replaces
/// the area directory and the instance time zone source. Calendar tools stay hidden until HA reports calendars;
/// music tools exist only with <c>music.speakers</c>.
/// </summary>
public static class HomeAssistantServiceCollectionExtensions
{
    public const string Section = "ha";

    public static IServiceCollection AddMarshalHomeAssistant(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<HomeAssistantOptions>().Bind(configuration.GetSection(Section))
            .Validate(options => options.Url is null || options.Url.IsAbsoluteUri && (options.Url.Scheme == Uri.UriSchemeHttp || options.Url.Scheme == Uri.UriSchemeHttps), "ha.url must be an absolute http(s) URL.")
            .Validate(options => options.Url is null || !string.IsNullOrWhiteSpace(options.TokenSecret), "ha.tokenSecret is required when ha.url is set.")
            .ValidateOnStart();

        services.AddHttpClient(HomeAssistantClient.HttpClientName);
        services.AddSingleton<IHomeAssistantClient, HomeAssistantClient>();
        services.AddSingleton<IHomeAssistantSocketFactory, ClientWebSocketFactory>();
        services.AddSingleton(new HomeAssistantCacheTimings());
        services.AddSingleton<HomeAssistantEventSource>();
        services.AddSingleton<IEventSource>(provider => provider.GetRequiredService<HomeAssistantEventSource>());
        services.AddSingleton<HomeAssistantCache>();
        services.AddSingleton<IHomeAssistantCache>(provider => provider.GetRequiredService<HomeAssistantCache>());
        services.AddSingleton<IHostedService>(provider => provider.GetRequiredService<HomeAssistantCache>());
        services.AddSingleton<IHomeAssistantCommands>(provider => provider.GetRequiredService<HomeAssistantCache>());

        services.AddSingleton<IConditionEvaluator, HaStateConditionEvaluator>();
        services.AddSingleton<IPresenceProvider, HaPresenceProvider>();
        services.AddSingleton<INotificationChannel, HaVoiceAnnounceChannel>();
        services.AddSingleton<INotificationChannel, HaVoiceConversationChannel>();
        services.AddSingleton<INotificationChannel, HaPushChannel>();
        services.AddSingleton<IDisplayPlayer, HomeAssistantDisplayPlayer>();
        services.AddSingleton<IRiskTargetResolver, HaTargetResolver>();
        services.AddSingleton<IContextContributor, HaAreaContextContributor>();
        services.AddSingleton<IToolNamespaceHints, HaNamespaceHints>();
        services.AddSingleton<IToolNamespaceHints, MusicNamespaceHints>();
        services.Replace(ServiceDescriptor.Singleton<IAreaDirectory, HaAreaDirectory>());
        services.Replace(ServiceDescriptor.Singleton<IInstanceTimeZone>(provider =>
        {
            string? configured = provider.GetRequiredService<IConfiguration>()["instance:timeZone"];
            return new HomeAssistantTimeZoneSource(
                string.IsNullOrWhiteSpace(configured) ? null : configured,
                provider.GetRequiredService<IHomeAssistantCache>(),
                provider.GetRequiredService<IOptions<HomeAssistantOptions>>().Value.Url is null ? null : provider.GetRequiredService<IHomeAssistantClient>());
        }));

        services.AddSingleton<IToolSource, TimerToolSource>();
        services.AddSingleton<IToolSource, PlacesToolSource>();
        services.AddSingleton<IToolSource, EnergyToolSource>();
        services.AddSingleton<ICalendarProvider, HomeAssistantCalendarProvider>();
        services.AddSingleton<IToolSource, CalendarToolSource>();
        services.AddSingleton<IMusicPlayer, HomeAssistantMusicPlayer>();
        services.AddSingleton<IToolSource, MusicToolSource>();
        services.AddSingleton<Marshal.Abstractions.Widgets.IWidgetDataProvider, MusicWidgetData>();
        return services;
    }
}
