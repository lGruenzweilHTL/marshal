using System.Collections.Immutable;
using System.Text.Json;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>An area from HA's area registry.</summary>
public sealed record HaArea(string Id, string Name, IReadOnlyList<string> Aliases, string? FloorId);

/// <summary>A floor from HA's floor registry.</summary>
public sealed record HaFloor(string Id, string Name, IReadOnlyList<string> Aliases);

/// <summary>A device from HA's device registry (<c>name_by_user</c> wins over <c>name</c>).</summary>
public sealed record HaDevice(string Id, string? Name, string? AreaId);

/// <summary>
/// An entity as Marshal sees it: registry data merged with its device (area) and state (name, device class).
/// </summary>
/// <param name="EntityId">Entity id (<c>light.kitchen</c>).</param>
/// <param name="Name">Registry name, else friendly name, else original name, else the object id.</param>
/// <param name="Aliases">Assist aliases (exposed entities only).</param>
/// <param name="AreaId">Entity area, else its device's area.</param>
/// <param name="DeviceId">Device id, if any.</param>
/// <param name="DeviceClass">Device class (registry override, original, or state attribute).</param>
/// <param name="Exposed">Exposed to Assist (conversation).</param>
public sealed record HaEntity(string EntityId, string Name, IReadOnlyList<string> Aliases, string? AreaId, string? DeviceId, string? DeviceClass, bool Exposed)
{
    /// <summary>Domain part of the entity id.</summary>
    public string Domain => EntityId[..EntityId.IndexOf('.', StringComparison.Ordinal)];
}

/// <summary>Current state of an entity.</summary>
public sealed record HaState(string EntityId, string State, JsonElement Attributes, DateTimeOffset? LastUpdated)
{
    /// <summary>The <c>friendly_name</c> attribute.</summary>
    public string? FriendlyName =>
        Attributes.ValueKind == JsonValueKind.Object && Attributes.TryGetProperty("friendly_name", out JsonElement name) && name.ValueKind == JsonValueKind.String
            ? name.GetString()
            : null;
}

/// <summary>
/// Immutable view of HA's registries, exposure and states at one moment.
/// </summary>
public sealed record HaSnapshot
{
    public required ImmutableDictionary<string, HaArea> Areas { get; init; }

    public required ImmutableDictionary<string, HaFloor> Floors { get; init; }

    public required ImmutableDictionary<string, HaDevice> Devices { get; init; }

    public required ImmutableDictionary<string, HaEntity> Entities { get; init; }

    public required ImmutableDictionary<string, HaState> States { get; init; }

    /// <summary>HA's configured IANA time zone.</summary>
    public string? TimeZone { get; init; }
}
