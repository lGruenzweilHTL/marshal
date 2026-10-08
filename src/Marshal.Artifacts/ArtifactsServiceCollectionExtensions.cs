using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Artifacts;

/// <summary>
/// Registers the artifact store (<c>artifacts</c> in <c>marshal.yaml</c>) in the artifact directory
/// (<c>/data/artifacts</c>) and the <c>artifacts.*</c> tools. The host provides <see cref="IArtifactLinks"/>.
/// </summary>
public static class ArtifactsServiceCollectionExtensions
{
    public static IServiceCollection AddMarshalArtifacts(this IServiceCollection services, IConfiguration configuration, Func<IServiceProvider, string> artifactDirectory)
    {
        services.AddOptions<ArtifactOptions>().Bind(configuration.GetSection("artifacts"))
            .Validate(options => options.QuotaMb >= 1, "artifacts.quotaMb must be at least 1.")
            .Validate(options => options.MaxUploadMb >= 1, "artifacts.maxUploadMb must be at least 1.")
            .Validate(options => options.SignedLinkMinutes is >= 1 and <= 1440, "artifacts.signedLinkMinutes must be between 1 and 1440.")
            .ValidateOnStart();

        services.AddSingleton(provider => new ArtifactDirectory(artifactDirectory(provider)));
        services.AddSingleton<IArtifactKindDetector, ArtifactKindDetector>();
        services.AddSingleton<IArtifactStore, FileArtifactStore>();
        services.AddSingleton<IToolSource, ArtifactToolSource>();
        services.AddSingleton(new LiveTopic(FileArtifactStore.Topic));
        return services;
    }
}
