namespace Marshal.Abstractions.Calendar;

/// <summary>Expected failures of calendar providers.</summary>
public static class CalendarErrors
{
    public static Error NotFound(string calendar) => new("calendar.not_found", $"There is no calendar '{calendar}'.", ErrorType.NotFound);

    public static Error Unsupported(string calendar, string change) =>
        new("calendar.unsupported", $"Calendar '{calendar}' does not support {change} events.", ErrorType.Validation);

    public static Error Failed(string message) => new("calendar.failed", message, ErrorType.Unavailable);
}
