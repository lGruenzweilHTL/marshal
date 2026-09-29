using Marshal.Abstractions.Rss;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.FreshRss.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Integrations.FreshRss;

/// <summary>
/// Registers the FreshRSS integration (<c>freshrss</c> in <c>marshal.yaml</c>): the client, the full-text extractor
/// and the RSS tools, which stay hidden until FreshRSS is configured.
/// </summary>
public static class FreshRssServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalFreshRss(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<FreshRssOptions>().Bind(configuration.GetSection("freshrss"))
            .Validate(options => options.Url is null || options.Url is { IsAbsoluteUri: true } url && (url.Scheme == Uri.UriSchemeHttp || url.Scheme == Uri.UriSchemeHttps), "freshrss.url must be an absolute http(s) URL.")
            .Validate(options => options.Url is null || options.UserName.Length > 0, "freshrss.userName is required.")
            .Validate(options => options.SearchWindow is >= 1 and <= 5000 && options.PageSize is >= 1 and <= 1000, "freshrss.searchWindow must be 1-5000 and pageSize 1-1000.")
            .Validate(options => options.FullText is { MaxBytes: >= 1024, MaxCharacters: >= 100 } && options.FullText.Timeout > TimeSpan.Zero, "freshrss.fullText needs maxBytes >= 1024, maxCharacters >= 100 and a positive timeout.")
            .ValidateOnStart();

        // Edits (mark read) are idempotent, so retries are safe for every method.
        services.AddHttpClient(FreshRssClient.HttpClientName).AddStandardResilienceHandler();
        // The extractor enforces its own time limit; redirects stay limited to a few hops.
        services.AddHttpClient(SmartReaderExtractor.HttpClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { MaxAutomaticRedirections = 5 });

        services.AddSingleton<IRssClient, FreshRssClient>();
        services.AddSingleton<IFullTextExtractor, SmartReaderExtractor>();
        services.AddSingleton<IToolSource, RssToolSource>();
        return services;
    }
}
