using System.Globalization;
using Marshal.Abstractions;

namespace Marshal.Memory;

/// <summary>
/// Parses valid-time arguments: an ISO date (<c>2026-03-01</c>, 00:00 in the owner's time zone) or an ISO date-time,
/// with an offset or in the owner's time zone without one.
/// </summary>
public static class ValidTimeInput
{
    private static readonly string[] _localFormats = ["yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd'T'HH:mm:ss.FFFFFFF"];

    /// <summary>The instant, null for null input, or a validation error naming <paramref name="argument"/>.</summary>
    public static Result<DateTimeOffset?> Parse(string? text, TimeZoneInfo zone, string argument)
    {
        if (text is null)
            return Result.Success<DateTimeOffset?>(null);

        string value = text.Trim();
        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out DateOnly date))
            return Local(date.ToDateTime(TimeOnly.MinValue), zone);
        if (DateTime.TryParseExact(value, _localFormats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
            return Local(local, zone);
        if (value.Contains('T', StringComparison.Ordinal)
            && DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset instant))
        {
            return Result.Success<DateTimeOffset?>(instant.ToUniversalTime());
        }

        return new Error("memory.invalid_arguments", $"{argument} must be a date (YYYY-MM-DD) or an ISO 8601 date-time.", ErrorType.Validation);
    }

    // A local time in a DST gap moves forward; an ambiguous one takes the standard-time offset.
    private static Result<DateTimeOffset?> Local(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        while (zone.IsInvalidTime(unspecified))
            unspecified = unspecified.AddMinutes(30);

        return Result.Success<DateTimeOffset?>(new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime());
    }
}
