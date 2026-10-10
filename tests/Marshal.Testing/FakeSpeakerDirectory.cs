using Marshal.Abstractions.Music;

namespace Marshal.Testing;

/// <summary>Speakers by name; satellites map to a speaker name through <see cref="Satellites"/>.</summary>
public sealed class FakeSpeakerDirectory(params (string Name, string Entity)[] speakers) : ISpeakerDirectory
{
    private readonly Dictionary<string, string> _speakers = speakers.ToDictionary(speaker => speaker.Name, speaker => speaker.Entity, StringComparer.Ordinal);

    /// <summary>Speaker name per satellite id.</summary>
    public Dictionary<string, string> Satellites { get; } = new(StringComparer.Ordinal);

    public IReadOnlyList<string> Names => [.. _speakers.Keys.Order(StringComparer.Ordinal)];

    public MusicSpeaker? ForSatellite(string? satelliteId) =>
        satelliteId is not null && Satellites.TryGetValue(satelliteId, out string? name) ? ByName(name) : null;

    public MusicSpeaker? ByName(string name) => _speakers.TryGetValue(name, out string? entity) ? new MusicSpeaker(name, entity) : null;
}
