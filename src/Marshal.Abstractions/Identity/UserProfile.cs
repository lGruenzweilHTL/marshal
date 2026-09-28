namespace Marshal.Abstractions.Identity;

/// <summary>
/// What prompts and schedules need to know about a user.
/// </summary>
/// <param name="OwnerId">The user.</param>
/// <param name="DisplayName">Name to address the user by.</param>
/// <param name="Language">Preferred language for runs without a request language.</param>
/// <param name="TimeZone">IANA time zone id.</param>
public sealed record UserProfile(OwnerId OwnerId, string DisplayName, string Language, string TimeZone);
