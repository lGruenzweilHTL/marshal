using Marshal.Abstractions.Secrets;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marshal.Integrations.GitHub;

/// <summary>
/// Registers the GitHub integration (<c>github</c> in <c>marshal.yaml</c>): the read-only client and the
/// <c>repos.*</c> tools, which stay hidden until repositories are configured.
/// </summary>
public static class GitHubServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalGitHub(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GitHubOptions>().Bind(configuration.GetSection("github"))
            .Validate(options => options.Repos.Keys.All(GitHubOptions.IsRepoId), "github.repos ids must be lowercase letters, digits and dashes, starting with a letter.")
            .Validate(options => options.Repos.Values.All(GitHubOptions.IsFullName), "github.repos values must be owner/name.")
            .Validate(options => options.Repos.Count == 0 || SecretName.IsValid(options.TokenSecret), "github.tokenSecret must name a secret when repos are configured.")
            .ValidateOnStart();

        // Reads only, so retries are safe. Log downloads answer with a redirect that is followed by hand, without the token.
        services.AddHttpClient(GitHubRestClient.HttpClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false })
            .AddStandardResilienceHandler();
        services.AddHttpClient(GitHubRestClient.DownloadClientName)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler { AllowAutoRedirect = false });
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IGitHubClient, GitHubRestClient>();
        services.AddSingleton<IToolSource, RepoToolSource>();
        return services;
    }
}
