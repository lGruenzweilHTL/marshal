namespace Marshal.Abstractions.Calendar;

/// <summary>Expected failures of calendar providers.</summary>
public static class CalendarErrors
{
    public static Error NotFound(string calendar) => new("calendar.not_found", $"There is no calendar '{calendar}'.", ErrorType.NotFound);

    public static Error Unsupported(string calendar, string change) =>
        new("calendar.unsupported", $"Calendar '{calendar}' does not support {change} events.", ErrorType.Validation);

    public static Error EventNotFound(string eventId) => new("calendar.event_not_found", $"There is no event '{eventId}'.", ErrorType.NotFound);

    public static Error Failed(string message) => new("calendar.failed", message, ErrorType.Unavailable);

    public static Error Invalid(string message) => new("calendar.invalid_arguments", message, ErrorType.Validation);

    public static Error ScopeUnsupported(string calendar) =>
        Invalid($"scope is not supported for calendar '{calendar}': it cannot change single occurrences of recurring events.");
}
