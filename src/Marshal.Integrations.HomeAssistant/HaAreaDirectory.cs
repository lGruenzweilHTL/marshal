using Marshal.Abstractions.Satellites;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Area and floor names from HA's registries; unknown ids (or a cache that is not loaded) keep the id.
/// </summary>
public sealed class HaAreaDirectory(IHomeAssistantCache cache) : IAreaDirectory
{
    public Task<(string? AreaName, string? FloorName)> DescribeAsync(string areaId, CancellationToken cancellationToken)
    {
        if (cache.Current is not { } snapshot || !snapshot.Areas.TryGetValue(areaId, out HaArea? area))
            return Task.FromResult<(string?, string?)>((areaId.Replace('_', ' '), null));

        string? floor = area.FloorId is { } id ? snapshot.Floors.GetValueOrDefault(id)?.Name : null;
        return Task.FromResult<(string?, string?)>((area.Name, floor));
    }
}
