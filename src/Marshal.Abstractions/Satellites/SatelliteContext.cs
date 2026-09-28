namespace Marshal.Abstractions.Satellites;

/// <summary>
/// Where a turn was spoken: profile plus area and floor names.
/// </summary>
/// <param name="SatelliteId">Satellite entity id, if any.</param>
/// <param name="Profile">The satellite's profile (defaults when unconfigured).</param>
/// <param name="AreaName">Human-readable area name, if the turn carries an area.</param>
/// <param name="FloorName">Human-readable floor name, if known.</param>
public sealed record SatelliteContext(string? SatelliteId, SatelliteProfile Profile, string? AreaName, string? FloorName);
