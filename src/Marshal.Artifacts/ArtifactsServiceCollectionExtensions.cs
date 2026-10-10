using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.LiveEvents;
using Marshal.Abstractions.Tools;
using Marshal.Artifacts.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Artifacts;

/// <summary>
/// Registers the artifact store (<c>artifacts</c> in <c>marshal.yaml</c>) in the artifact directory
/// (<c>/data/artifacts</c>) and the <c>artifacts.*</c> tools. The host provides <see cref="IArtifactLinks"/>,
/// <see cref="IDisplayLinks"/> and <see cref="Abstractions.Displays.IDisplayDirectory"/>; Home Assistant the display player.
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
        // 3D model pages take long to draw, so they are cached next to the artifacts (/data/cache/renditions).
        services.AddSingleton(provider => new SkiaArtifactRenditions(
            provider.GetRequiredService<IArtifactStore>(),
            new FileRenditionCache(RenditionCacheDirectory(provider.GetRequiredService<ArtifactDirectory>().Path))));
        services.AddSingleton<IArtifactRenditions>(provider => provider.GetRequiredService<SkiaArtifactRenditions>());
        services.AddSingleton<IArtifactPreviews>(provider => provider.GetRequiredService<SkiaArtifactRenditions>());
        services.AddSingleton<ArtifactInspector>();
        services.AddSingleton<ArtifactDisplays>();
        services.AddSingleton<IToolSource, ArtifactToolSource>();
        services.AddSingleton(new LiveTopic(FileArtifactStore.Topic));
        return services;
    }

    private static string RenditionCacheDirectory(string artifactDirectory) =>
        Path.Combine(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(artifactDirectory)) ?? artifactDirectory, "cache", "renditions");
}
