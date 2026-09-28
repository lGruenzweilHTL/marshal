namespace Marshal.Abstractions.Identity;

/// <summary>
/// What a client tells us about who is speaking. Stored with the session so it can be re-attributed later.
/// </summary>
public sealed record IdentityEvidence(string? HaUserId, string? DeviceId, string? SatelliteId, string? AreaId);
