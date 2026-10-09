using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Displays;
using Marshal.Abstractions.Execution;

namespace Marshal.Artifacts;

/// <summary>
/// What <c>artifacts.show</c> needs to put an artifact on a screen: renditions, display links, the satellite's display
/// and a player. Without an available player (Home Assistant not configured) no turn has a display.
/// </summary>
public sealed class ArtifactDisplays(IArtifactRenditions renditions, IDisplayLinks links, IDisplayDirectory directory, IEnumerable<IDisplayPlayer> players)
{
    private readonly IDisplayPlayer[] _players = [.. players];

    public IArtifactRenditions Renditions => renditions;

    public IDisplayLinks Links => links;

    /// <summary>The display of the turn's satellite and the player for it; null when the turn has none.</summary>
    public (DisplaySurface Display, IDisplayPlayer Player)? For(MarshalExecutionContext context) =>
        _players.FirstOrDefault(player => player.IsAvailable) is { } player && directory.ForSatellite(context.SatelliteId) is { } display ? (display, player) : null;
}
