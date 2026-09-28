using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// Expands the targeting arguments of HA MCP tools (<c>name</c>, <c>area</c>, <c>floor</c>, <c>domain</c>,
/// <c>device_class</c>) to the exposed entities they address, the way HA's intent matching does: every given
/// argument narrows the set. Each target carries <c>domain</c> and, when known, <c>deviceClass</c> for risk rules.
/// A name, area or floor that matches nothing, an empty result, an argument of an unexpected shape or a cache that
/// is not loaded is unresolved, which the risk engine treats as <c>confirm</c>. A call without targeting arguments
/// (e.g. <c>GetLiveContext</c>) yields one target without attributes, so only rules without target conditions match.
/// </summary>
public sealed class HaTargetResolver(IHomeAssistantCache cache) : IRiskTargetResolver
{
    private static readonly string[] _targetArguments = ["name", "area", "floor", "domain", "device_class"];
    private static readonly RiskTarget _untargeted = new("ha:untargeted", new Dictionary<string, string>());

    public string Namespace => "ha";

    public Task<Result<IReadOnlyList<RiskTarget>>> ResolveAsync(ToolId tool, JsonElement arguments, CancellationToken cancellationToken) =>
        Task.FromResult(Resolve(arguments));

    private Result<IReadOnlyList<RiskTarget>> Resolve(JsonElement arguments)
    {
        if (cache.Current is not { } snapshot)
            return Unresolved("Home Assistant data is not loaded.");

        if (arguments.ValueKind != JsonValueKind.Object)
            return Unresolved("Arguments are not an object.");

        if (!_targetArguments.Any(name => arguments.TryGetProperty(name, out _)))
            return new[] { _untargeted };

        IEnumerable<HaEntity> entities = snapshot.Entities.Values.Where(entity => entity.Exposed);

        if (arguments.TryGetProperty("name", out JsonElement name))
        {
            if (name.ValueKind != JsonValueKind.String)
                return Unresolved("name is not a string.");

            string wanted = name.GetString()!.Trim();
            entities = entities.Where(entity => Names(entity, snapshot).Contains(wanted, StringComparer.OrdinalIgnoreCase));
        }

        if (arguments.TryGetProperty("area", out JsonElement areaName))
        {
            if (areaName.ValueKind != JsonValueKind.String)
                return Unresolved("area is not a string.");

            string wanted = areaName.GetString()!.Trim();
            HaArea? area = snapshot.Areas.Values.FirstOrDefault(candidate => Matches(wanted, candidate.Id, candidate.Name, candidate.Aliases));
            if (area is null)
                return Unresolved($"Unknown area '{wanted}'.");

            entities = entities.Where(entity => entity.AreaId == area.Id);
        }

        if (arguments.TryGetProperty("floor", out JsonElement floorName))
        {
            if (floorName.ValueKind != JsonValueKind.String)
                return Unresolved("floor is not a string.");

            string wanted = floorName.GetString()!.Trim();
            HaFloor? floor = snapshot.Floors.Values.FirstOrDefault(candidate => Matches(wanted, candidate.Id, candidate.Name, candidate.Aliases));
            if (floor is null)
                return Unresolved($"Unknown floor '{wanted}'.");

            entities = entities.Where(entity => entity.AreaId is { } id && snapshot.Areas.GetValueOrDefault(id)?.FloorId == floor.Id);
        }

        foreach ((string argument, Func<HaEntity, string?> value) in new (string, Func<HaEntity, string?>)[] { ("domain", entity => entity.Domain), ("device_class", entity => entity.DeviceClass) })
        {
            if (!arguments.TryGetProperty(argument, out JsonElement filter))
                continue;

            if (Values(filter) is not { } allowed)
                return Unresolved($"{argument} is not a string or a list of strings.");

            entities = entities.Where(entity => value(entity) is { } actual && allowed.Contains(actual, StringComparer.OrdinalIgnoreCase));
        }

        List<RiskTarget> targets = [.. entities.OrderBy(entity => entity.EntityId, StringComparer.Ordinal).Select(Target)];
        return targets.Count == 0 ? Unresolved("No exposed entity matches.") : targets;
    }

    private static IEnumerable<string> Names(HaEntity entity, HaSnapshot snapshot)
    {
        yield return entity.Name;
        if (snapshot.States.GetValueOrDefault(entity.EntityId)?.FriendlyName is { } friendly)
            yield return friendly;

        foreach (string alias in entity.Aliases)
            yield return alias;
    }

    private static bool Matches(string wanted, string id, string name, IReadOnlyList<string> aliases) =>
        string.Equals(wanted, id, StringComparison.OrdinalIgnoreCase)
        || string.Equals(wanted, name, StringComparison.OrdinalIgnoreCase)
        || aliases.Contains(wanted, StringComparer.OrdinalIgnoreCase);

    private static List<string>? Values(JsonElement filter) => filter.ValueKind switch
    {
        JsonValueKind.String => [filter.GetString()!],
        JsonValueKind.Array when filter.EnumerateArray().All(item => item.ValueKind == JsonValueKind.String) => [.. filter.EnumerateArray().Select(item => item.GetString()!)],
        _ => null,
    };

    private static RiskTarget Target(HaEntity entity)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal) { ["domain"] = entity.Domain };
        if (entity.DeviceClass is { } deviceClass)
            attributes["deviceClass"] = deviceClass;
        if (entity.AreaId is { } area)
            attributes["area"] = area;

        return new RiskTarget(entity.EntityId, attributes);
    }

    private static Error Unresolved(string message) => new("ha.unresolved_target", message, ErrorType.NotFound);
}
