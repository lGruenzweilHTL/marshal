using Marshal.Abstractions.Displays;
using Marshal.Abstractions.Satellites;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Displays;

/// <summary>Displays from <c>displays.surfaces</c>, mapped to satellites through their profile's <c>display</c>.</summary>
public sealed class ConfiguredDisplayDirectory(IOptions<DisplayOptions> options, ISatelliteContextProvider satellites) : IDisplayDirectory
{
    public DisplaySurface? ForSatellite(string? satelliteId)
    {
        if (satelliteId is null || satellites.GetProfile(satelliteId).Display is not { } name)
            return null;

        return options.Value.Surfaces.TryGetValue(name, out DisplaySurfaceOptions? surface) ? new DisplaySurface(name, surface.Entity) : null;
    }
}
