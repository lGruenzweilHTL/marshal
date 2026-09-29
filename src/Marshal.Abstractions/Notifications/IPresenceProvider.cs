using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Notifications;

/// <summary>
/// Where a person is: home or away, and near which satellite.
/// </summary>
public interface IPresenceProvider
{
    Task<Presence> GetAsync(OwnerId owner, CancellationToken cancellationToken);
}

/// <summary>A person's presence.</summary>
/// <param name="State">Home, away or unknown.</param>
/// <param name="Satellite">Satellite in the occupied room, when home and a room is occupied.</param>
public sealed record Presence(PresenceState State, string? Satellite = null)
{
    public static Presence Unknown { get; } = new(PresenceState.Unknown);
}

/// <summary>Home or away.</summary>
public enum PresenceState
{
    Unknown,
    Home,
    Away,
}
