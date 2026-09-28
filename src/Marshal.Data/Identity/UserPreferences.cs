using Marshal.Abstractions.Identity;

namespace Marshal.Data.Identity;

/// <summary>
/// Per-user preferences, keyed by the user id.
/// </summary>
public sealed class UserPreferences
{
    public required OwnerId UserId { get; init; }

    public required string Language { get; set; }

    /// <summary>IANA time zone id.</summary>
    public required string TimeZone { get; set; }

    public string? QuietHours { get; set; }

    public string? NotificationTargets { get; set; }

    public DateTimeOffset? DndUntil { get; set; }
}
