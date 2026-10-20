namespace Marshal.Abstractions.Identity;

/// <summary>
/// Per-user settings. Content preferences (briefing topics, interests) are not here; they are memory chunks.
/// </summary>
/// <param name="Language"><c>de</c> or <c>en</c>; used when a request has no language.</param>
/// <param name="TimeZone">IANA time zone id.</param>
/// <param name="QuietHours">Weekly quiet-hour windows.</param>
/// <param name="NotificationTargets">Where notifications reach the user.</param>
/// <param name="DndUntil">Do-not-disturb end, if active.</param>
/// <param name="HelpMarshalLearn">
/// "Help Marshal learn" (default off): gates every feature that learns from the user's behavior or proposes things on
/// its own, such as rated runs becoming eval cases.
/// </param>
public sealed record UserPreferencesRecord(
    string Language,
    string TimeZone,
    IReadOnlyList<QuietHoursWindow> QuietHours,
    NotificationTargets NotificationTargets,
    DateTimeOffset? DndUntil,
    bool HelpMarshalLearn = false);

/// <summary>
/// One quiet-hour window starting on <paramref name="Day"/>; an <paramref name="End"/> before <paramref name="Start"/>
/// ends on the next day.
/// </summary>
/// <param name="Day">Day the window starts.</param>
/// <param name="Start">Local start time.</param>
/// <param name="End">Local end time.</param>
public sealed record QuietHoursWindow(DayOfWeek Day, TimeOnly Start, TimeOnly End);

/// <summary>
/// Notification targets of a user.
/// </summary>
/// <param name="Person">Home Assistant <c>person.*</c> entity (presence, which satellite is near).</param>
/// <param name="Push">Home Assistant notify services for push, e.g. <c>notify.mobile_app_pixel</c>.</param>
public sealed record NotificationTargets(string? Person, IReadOnlyList<string> Push)
{
    /// <summary>No targets.</summary>
    public static NotificationTargets None { get; } = new(null, []);
}
