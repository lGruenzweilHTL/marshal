using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Identity;

namespace Marshal.Core.Identity;

/// <summary>
/// Validation of user preferences and IANA time zone ids.
/// </summary>
public static partial class UserPreferencesRules
{
    /// <summary>True for an IANA time zone id known to this system (e.g. <c>Europe/Vienna</c>, <c>UTC</c>).</summary>
    public static bool IsIanaTimeZone(string? id) =>
        !string.IsNullOrWhiteSpace(id) && TimeZoneInfo.TryFindSystemTimeZoneById(id, out TimeZoneInfo? zone) && zone.HasIanaId;

    public static Result Validate(UserPreferencesRecord preferences)
    {
        var errors = new List<string>();
        if (preferences.Language is not ("de" or "en"))
            errors.Add("language must be 'de' or 'en'.");
        if (!IsIanaTimeZone(preferences.TimeZone))
            errors.Add($"timeZone '{preferences.TimeZone}' is not an IANA time zone.");
        if (preferences.QuietHours.Any(window => window.Start == window.End))
            errors.Add("quietHours windows need different start and end times.");
        if (preferences.NotificationTargets.Person is { } person && !PersonPattern().IsMatch(person))
            errors.Add("notificationTargets.person must be a person.* entity id.");
        if (preferences.NotificationTargets.Push.Any(target => !NotifyPattern().IsMatch(target)))
            errors.Add("notificationTargets.push entries must be notify.* services.");

        return errors.Count == 0 ? Result.Success() : new Error("preferences.invalid", string.Join(" ", errors), ErrorType.Validation);
    }

    [GeneratedRegex("^person\\.[a-z0-9_]+$")]
    private static partial Regex PersonPattern();

    [GeneratedRegex("^notify\\.[a-z0-9_]+$")]
    private static partial Regex NotifyPattern();
}
