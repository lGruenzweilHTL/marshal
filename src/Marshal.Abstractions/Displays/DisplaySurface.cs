namespace Marshal.Abstractions.Displays;

/// <summary>A configured screen: a Home Assistant media player that plays media renditions of artifacts.</summary>
/// <param name="Name">Display name from <c>displays.surfaces</c>.</param>
/// <param name="Entity">The <c>media_player</c> entity id.</param>
public sealed record DisplaySurface(string Name, string Entity);

/// <summary>What a media renderer is told it plays.</summary>
public enum DisplayMediaType
{
    Image,
    Video,
}
