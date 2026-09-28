using Marshal.Abstractions.Execution;

namespace Marshal.Abstractions.Satellites;

/// <summary>
/// Provides the satellite profile and area context of a turn.
/// </summary>
public interface ISatelliteContextProvider
{
    /// <summary>The profile of a satellite; defaults for unknown satellites and null.</summary>
    SatelliteProfile GetProfile(string? satelliteId);

    /// <summary>Profile plus area and floor names for the turn.</summary>
    Task<SatelliteContext> GetContextAsync(MarshalExecutionContext context, CancellationToken cancellationToken);
}
