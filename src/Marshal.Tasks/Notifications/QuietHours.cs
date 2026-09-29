using Marshal.Abstractions.Identity;

namespace Marshal.Tasks.Notifications;

/// <summary>
/// Weekly quiet-hour windows in the owner's local time.
/// </summary>
internal static class QuietHours
{
    /// <summary>When the window containing <paramref name="now"/> ends (UTC), or null outside quiet hours.</summary>
    public static DateTimeOffset? ActiveUntil(IReadOnlyList<QuietHoursWindow> windows, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        DateTime? end = null;
        foreach (QuietHoursWindow window in windows)
        {
            // A window may have started today or (overnight) yesterday.
            foreach (DateTime day in new[] { local.Date, local.Date.AddDays(-1) })
            {
                if (day.DayOfWeek != window.Day)
                    continue;
                DateTime start = day + window.Start.ToTimeSpan();
                DateTime stop = day + window.End.ToTimeSpan();
                if (stop <= start)
                    stop = stop.AddDays(1);
                if (local >= start && local < stop && (end is null || stop > end))
                    end = stop;
            }
        }

        return end is { } localEnd ? new DateTimeOffset(localEnd, zone.GetUtcOffset(localEnd)).ToUniversalTime() : null;
    }
}
