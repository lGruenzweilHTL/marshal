using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Tools;
using Marshal.Abstractions.Web;
using Marshal.Integrations.Web.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marshal.Integrations.Web;

/// <summary>
/// Registers web search (<c>web.searxng</c> in <c>marshal.yaml</c>): the SearXNG provider, the per-session result store,
/// the <c>web.*</c> tools and the <c>web:</c> extraction references. The tools stay hidden until SearXNG is configured.
/// </summary>
public static class WebServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalWeb(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<SearxngOptions>().Bind(configuration.GetSection(SearxngOptions.Section))
            .Validate(options => options.Url is null || options.Url is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps), "web.searxng.url must be an absolute http(s) URL.")
            .Validate(options => options.AuthHeaderSecret is null || SecretName.IsValid(options.AuthHeaderSecret), "web.searxng.authHeaderSecret must be a secret name (^[a-z][a-z0-9_]{0,63}$).")
            .Validate(options => options.MaxResults is >= 1 and <= 20, "web.searxng.maxResults must be 1-20.")
            .Validate(options => options.Engines is null || options.Engines.TrueForAll(engine => !string.IsNullOrWhiteSpace(engine)), "web.searxng.engines must not contain empty names.")
            .ValidateOnStart();

        // Searches are reads, so retries are safe.
        services.AddHttpClient(SearxngSearchProvider.HttpClientName).AddStandardResilienceHandler();

        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IWebSearchProvider, SearxngSearchProvider>();
        services.AddSingleton<ISessionWebResults, SessionWebResults>();
        services.AddSingleton<IToolSource, WebToolSource>();
        services.AddSingleton<IUntrustedContentResolver, WebContentResolver>();
        return services;
    }
}
