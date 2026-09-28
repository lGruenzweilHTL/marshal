namespace Marshal.Core.Satellites;

/// <summary>
/// Human-readable area and floor names for an area id. Home Assistant provides them in the extensibility change.
/// </summary>
public interface IAreaDirectory
{
    Task<(string? AreaName, string? FloorName)> DescribeAsync(string areaId, CancellationToken cancellationToken);
}
