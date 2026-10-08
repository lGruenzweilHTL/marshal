using System.Globalization;
using Marshal.Abstractions;

namespace Marshal.Integrations.HomeAssistant.Energy;

/// <summary>A period in local time and its UTC boundaries (end exclusive).</summary>
internal sealed record EnergyWindow(DateTime LocalStart, DateTime LocalEnd, DateTimeOffset Start, DateTimeOffset End)
{
    public static EnergyWindow Create(DateTime localStart, DateTime localEnd, TimeZoneInfo zone) =>
        new(localStart, localEnd, ToUtc(localStart, zone), ToUtc(localEnd, zone));

    // Each boundary is converted on its own, so a period across a DST change is 23 or 25 hours long. A local time in
    // the spring-forward gap does not exist; the hour after it is used.
    private static DateTimeOffset ToUtc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddHours(1);
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(unspecified, zone), TimeSpan.Zero);
    }
}

/// <summary>The requested period and, with <c>compareWith: previous</c>, the one to compare it with.</summary>
internal sealed record EnergyPeriod(EnergyWindow Current, EnergyWindow? Previous);

/// <summary>
/// Resolves <c>energy.usage</c> periods in the owner's time zone. Open periods (<c>today</c>, <c>week</c>,
/// <c>month</c>) end at the current full hour, because HA's long-term statistics are hourly; their previous period
/// ends at the same elapsed time. Closed periods compare with the period just before: the day, week or month
/// before, or a range of the same number of days.
/// </summary>
internal static class EnergyPeriods
{
    public const string Today = "today";
    public const string Yesterday = "yesterday";
    public const string Week = "week";
    public const string Month = "month";
    public const string LastWeek = "last_week";
    public const string LastMonth = "last_month";
    public const string Range = "range";

    public static IReadOnlyList<string> All { get; } = [Today, Yesterday, Week, Month, LastWeek, LastMonth, Range];

    public static Result<EnergyPeriod> Resolve(string? period, string? from, string? to, bool comparePrevious, DateTimeOffset now, TimeZoneInfo zone)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, zone).DateTime;
        DateTime hour = new(local.Year, local.Month, local.Day, local.Hour, 0, 0, DateTimeKind.Unspecified);
        DateTime today = hour.Date;
        DateTime monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        DateTime firstOfMonth = new(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);

        (DateTime Start, DateTime End, Func<(DateTime, DateTime)> Previous)? resolved = period switch
        {
            Today => (today, hour, () => (today.AddDays(-1), hour.AddDays(-1))),
            Yesterday => (today.AddDays(-1), today, () => (today.AddDays(-2), today.AddDays(-1))),
            Week => (monday, hour, () => (monday.AddDays(-7), hour.AddDays(-7))),
            Month => (firstOfMonth, hour, () => (firstOfMonth.AddMonths(-1), SameTimeLastMonth(firstOfMonth, hour))),
            LastWeek => (monday.AddDays(-7), monday, () => (monday.AddDays(-14), monday.AddDays(-7))),
            LastMonth => (firstOfMonth.AddMonths(-1), firstOfMonth, () => (firstOfMonth.AddMonths(-2), firstOfMonth.AddMonths(-1))),
            _ => null,
        };

        if (period == Range)
        {
            if (Date(from) is not { } first || Date(to) is not { } last)
                return Invalid("A range needs from and to as dates like 2026-10-01.");
            if (first > last)
                return Invalid("from must not be after to.");

            var start = first.ToDateTime(TimeOnly.MinValue);
            var end = last.AddDays(1).ToDateTime(TimeOnly.MinValue);
            int days = last.DayNumber - first.DayNumber + 1;
            resolved = (start, end, () => (start.AddDays(-days), start));
        }

        if (resolved is not { } window)
            return Invalid($"period must be one of {string.Join(", ", All)}.");

        var current = EnergyWindow.Create(window.Start, window.End, zone);
        if (!comparePrevious)
            return new EnergyPeriod(current, null);

        (DateTime previousStart, DateTime previousEnd) = window.Previous();
        return new EnergyPeriod(current, EnergyWindow.Create(previousStart, previousEnd, zone));
    }

    // Last month up to the same day and hour, clamped to its last day (31 March → 28/29 February).
    private static DateTime SameTimeLastMonth(DateTime firstOfMonth, DateTime hour)
    {
        DateTime previous = firstOfMonth.AddMonths(-1);
        int day = Math.Min(hour.Day, DateTime.DaysInMonth(previous.Year, previous.Month));
        return new DateTime(previous.Year, previous.Month, day, hour.Hour, 0, 0, DateTimeKind.Unspecified);
    }

    private static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date) ? date : null;

    private static Error Invalid(string message) => new("energy.invalid_arguments", message, ErrorType.Validation);
}
