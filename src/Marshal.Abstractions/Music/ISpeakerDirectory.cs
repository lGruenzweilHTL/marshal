namespace Marshal.Abstractions.Music;

/// <summary>A configured music speaker: a Music Assistant player in Home Assistant.</summary>
/// <param name="Name">Speaker name from <c>music.speakers</c>.</param>
/// <param name="Entity">The <c>media_player</c> entity id.</param>
public sealed record MusicSpeaker(string Name, string Entity);

/// <summary>The configured music speakers and which one belongs to a satellite's room.</summary>
public interface ISpeakerDirectory
{
    /// <summary>All speaker names, sorted; empty without a <c>music</c> section.</summary>
    IReadOnlyList<string> Names { get; }

    /// <summary>The speaker named in the satellite's profile; null without satellite, profile or speaker.</summary>
    MusicSpeaker? ForSatellite(string? satelliteId);

    /// <summary>The speaker with this configured name; null for unknown names and entity ids.</summary>
    MusicSpeaker? ByName(string name);
}
