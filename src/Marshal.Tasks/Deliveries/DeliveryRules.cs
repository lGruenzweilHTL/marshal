using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Deliveries;

/// <summary>
/// What a task delivery may be, checked when a task is created or its deliveries change: never <c>critical</c>
/// (only system sources send that), no <c>low</c> voice, <c>followUp</c> and a target only on voice, valid
/// <c>state(...)</c> conditions (no areas), and <c>expires</c> together with a fallback channel.
/// </summary>
internal static class DeliveryRules
{
    public static Result Validate(IReadOnlyList<TaskDelivery> deliveries)
    {
        foreach (TaskDelivery delivery in deliveries)
        {
            Result valid = Validate(delivery);
            if (valid.IsFailure)
                return valid;
        }

        return Result.Success();
    }

    /// <summary>
    /// The deadline of <paramref name="expires"/> for a result ready at <paramref name="readyAt"/>: <c>HH:mm</c> is the
    /// next such local time (today or tomorrow), a local date and time is that time; both in <paramref name="zone"/>.
    /// </summary>
    public static DateTimeOffset? ExpiresAt(string expires, DateTimeOffset readyAt, TimeZoneInfo zone)
    {
        if (TimeOnly.TryParseExact(expires.Trim(), ["HH:mm", "H:mm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out TimeOnly time))
        {
            DateTime localNow = TimeZoneInfo.ConvertTime(readyAt, zone).DateTime;
            DateTime candidate = localNow.Date + time.ToTimeSpan();
            if (candidate <= localNow)
                candidate = candidate.AddDays(1);
            return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate)).ToUniversalTime();
        }

        if (DateTime.TryParse(expires.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
        {
            var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
            return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
        }

        return null;
    }

    private static Result Validate(TaskDelivery delivery)
    {
        if (delivery.Priority == NotificationPriority.Critical)
            return TaskErrors.Invalid("A delivery cannot be critical; only system sources send critical notifications.");
        if (delivery.Channel == DeliveryChannel.Voice && delivery.Priority == NotificationPriority.Low)
            return TaskErrors.Invalid("Low-priority deliveries never use voice; use inbox or push.");
        if (delivery.FollowUp && delivery.Channel != DeliveryChannel.Voice)
            return TaskErrors.Invalid("followUp needs the voice channel.");
        if (delivery.Target is not null && delivery.Channel != DeliveryChannel.Voice)
            return TaskErrors.Invalid("Only voice deliveries have a target.");

        foreach (string condition in delivery.When ?? [])
        {
            Result<StateCondition> parsed = StateCondition.Parse(condition);
            if (parsed.IsFailure)
                return TaskErrors.Invalid(parsed.Error.Message);
        }

        if (delivery.Expires is { } expires)
        {
            if (ExpiresAt(expires, DateTimeOffset.UnixEpoch, TimeZoneInfo.Utc) is null)
                return TaskErrors.Invalid($"'{expires}' is not a deadline like 11:00 or 2026-09-30T11:00.");
            if (delivery.Fallback is null)
                return TaskErrors.Invalid("A delivery with expires needs a fallback channel.");
        }
        else if (delivery.Fallback is not null)
        {
            return TaskErrors.Invalid("A fallback channel needs expires.");
        }

        if (delivery.Fallback == DeliveryChannel.Voice && delivery.Priority == NotificationPriority.Low)
            return TaskErrors.Invalid("Low-priority deliveries never use voice; use inbox or push.");

        return Result.Success();
    }
}
