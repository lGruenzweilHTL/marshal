using Marshal.Abstractions.Music;

namespace Marshal.Demo.HomeAssistant;

/// <summary>The simulated Music Assistant speakers (kitchen, living room, office); a satellite plays on the speaker in its area.</summary>
public sealed class DemoSpeakerDirectory(DemoWorld world) : ISpeakerDirectory
{
    private IEnumerable<MusicSpeaker> Speakers =>
        world.Home.Snapshot.Entities.Values
            .Where(entity => entity.Domain == "media_player" && entity.AreaId is not null)
            .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
            .Select(entity => new MusicSpeaker(entity.AreaId!, entity.EntityId));

    public IReadOnlyList<string> Names => [.. Speakers.Select(speaker => speaker.Name)];

    public MusicSpeaker? ForSatellite(string? satelliteId)
    {
        string? area = satelliteId is null ? null : world.Home.Snapshot.Entities.GetValueOrDefault(satelliteId)?.AreaId;
        return area is null ? null : Speakers.FirstOrDefault(speaker => speaker.Name == area);
    }

    public MusicSpeaker? ByName(string name) => Speakers.FirstOrDefault(speaker => string.Equals(speaker.Name, name, StringComparison.OrdinalIgnoreCase));
}
