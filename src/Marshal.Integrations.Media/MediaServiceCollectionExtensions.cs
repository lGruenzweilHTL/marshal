using Marshal.Abstractions.Media;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Integrations.Media;

/// <summary>
/// Registers the media integration (<c>media</c> in <c>marshal.yaml</c>): the Seerr, Sonarr and Radarr clients, the
/// Jellyfin links and the <c>media.*</c> tools, which stay hidden until <c>media.seerr.url</c> is set.
/// </summary>
public static class MediaServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalMedia(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<MediaOptions>().Bind(configuration.GetSection("media"))
            .Validate(options => options.Seerr?.Url is not null || (options.Sonarr is null && options.Radarr is null && options.JellyfinUrl is null), "media.seerr.url is required when a media section is configured.")
            .Validate(options => IsValid(options.Seerr), "media.seerr needs an absolute http(s) url and an apiKeySecret naming a secret.")
            .Validate(options => IsValid(options.Sonarr), "media.sonarr needs an absolute http(s) url and an apiKeySecret naming a secret.")
            .Validate(options => IsValid(options.Radarr), "media.radarr needs an absolute http(s) url and an apiKeySecret naming a secret.")
            .Validate(options => options.JellyfinUrl is null || IsHttp(options.JellyfinUrl), "media.jellyfinUrl must be an absolute http(s) URL.")
            .ValidateOnStart();

        // Retries only for reads: the request POST must not be repeated.
        services.AddHttpClient(SeerrRestClient.HttpClientName);
        services.AddHttpClient(ArrRestClient.HttpClientName).AddStandardResilienceHandler();
        services.AddSingleton<ISeerrClient, SeerrRestClient>();
        services.AddSingleton<IArrClient, ArrRestClient>();
        services.AddSingleton<IJellyfinLinks, JellyfinLinks>();
        services.AddSingleton<IToolSource, MediaToolSource>();
        return services;
    }

    private static bool IsValid(MediaService? service) =>
        service is null || (service.Url is { } url && IsHttp(url) && SecretName.IsValid(service.ApiKeySecret));

    private static bool IsHttp(Uri url) => url is { IsAbsoluteUri: true, Scheme: "http" or "https" };
}
