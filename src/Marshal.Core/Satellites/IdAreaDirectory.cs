namespace Marshal.Core.Satellites;

/// <summary>
/// v1 area names derived from the id (<c>living_room</c> → "living room"); no floors.
/// </summary>
public sealed class IdAreaDirectory : IAreaDirectory
{
    public Task<(string? AreaName, string? FloorName)> DescribeAsync(string areaId, CancellationToken cancellationToken) =>
        Task.FromResult<(string?, string?)>((areaId.Replace('_', ' '), null));
}
