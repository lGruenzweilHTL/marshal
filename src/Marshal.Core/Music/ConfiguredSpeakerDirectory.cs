using Marshal.Abstractions.Music;
using Marshal.Abstractions.Satellites;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Music;

/// <summary>Speakers from <c>music.speakers</c>, mapped to satellites through their profile's <c>speaker</c>.</summary>
public sealed class ConfiguredSpeakerDirectory(IOptions<MusicOptions> options, ISatelliteContextProvider satellites) : ISpeakerDirectory
{
    public IReadOnlyList<string> Names => [.. options.Value.Speakers.Keys.Order(StringComparer.Ordinal)];

    public MusicSpeaker? ForSatellite(string? satelliteId) =>
        satelliteId is not null && satellites.GetProfile(satelliteId).Speaker is { } name ? ByName(name) : null;

    public MusicSpeaker? ByName(string name) =>
        options.Value.Speakers.TryGetValue(name, out string? entity) ? new MusicSpeaker(name, entity) : null;
}
