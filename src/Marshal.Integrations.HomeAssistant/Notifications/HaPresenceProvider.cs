using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;

namespace Marshal.Integrations.HomeAssistant.Notifications;

/// <summary>
/// Presence from Home Assistant: the owner's <c>person.*</c> entity says home or away; at home, the most recently
/// changed occupied room (occupancy, presence or motion <c>binary_sensor</c> that is on) with an Assist satellite
/// is where to speak.
/// </summary>
public sealed class HaPresenceProvider(IHomeAssistantCache cache, IUserPreferencesStore preferences) : IPresenceProvider
{
    private static readonly HashSet<string> _roomSensors = ["occupancy", "presence", "motion"];

    public async Task<Presence> GetAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        string? person = (await preferences.GetAsync(owner, cancellationToken)).NotificationTargets.Person;
        if (person is null || cache.Current is not { } snapshot || snapshot.States.GetValueOrDefault(person)?.State is not { } state)
            return Presence.Unknown;
        if (state is "unknown" or "unavailable")
            return Presence.Unknown;
        if (state != "home")
            return new Presence(PresenceState.Away);

        string? satellite = snapshot.Entities.Values
            .Where(entity => entity.Domain == "binary_sensor" && entity.AreaId is not null && entity.DeviceClass is not null && _roomSensors.Contains(entity.DeviceClass))
            .Select(entity => (Entity: entity, State: snapshot.States.GetValueOrDefault(entity.EntityId)))
            .Where(sensor => sensor.State?.State == "on")
            .OrderByDescending(sensor => sensor.State!.LastUpdated)
            .Select(sensor => snapshot.Entities.Values
                .Where(entity => entity.Domain == "assist_satellite" && entity.AreaId == sensor.Entity.AreaId)
                .OrderBy(entity => entity.EntityId, StringComparer.Ordinal)
                .FirstOrDefault()?.EntityId)
            .FirstOrDefault(found => found is not null);
        return new Presence(PresenceState.Home, satellite);
    }
}
