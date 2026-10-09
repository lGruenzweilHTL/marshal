using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Satellites;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Satellites;

/// <summary>
/// Satellite profiles from the hot-reloaded <c>satellites.yaml</c>; unconfigured satellites use defaults.
/// </summary>
public sealed class YamlSatelliteContextProvider(IOptionsMonitor<SatellitesFile> file, IAreaDirectory areas) : ISatelliteContextProvider
{
    public SatelliteProfile GetProfile(string? satelliteId)
    {
        if (satelliteId is null || !file.CurrentValue.Satellites.TryGetValue(satelliteId, out SatelliteEntry? entry))
            return SatelliteProfile.Default;

        return new SatelliteProfile(entry.Verbosity, entry.Notes, entry.DenyTools, entry.DenyConfirm, entry.Display);
    }

    public async Task<SatelliteContext> GetContextAsync(MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        (string? area, string? floor) = context.AreaId is null ? (null, null) : await areas.DescribeAsync(context.AreaId, cancellationToken);
        return new SatelliteContext(context.SatelliteId, GetProfile(context.SatelliteId), area, floor);
    }
}
