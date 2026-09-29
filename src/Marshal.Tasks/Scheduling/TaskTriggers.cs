using System.Globalization;
using Cronos;
using Marshal.Abstractions;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Events;

namespace Marshal.Tasks.Scheduling;

/// <summary>
/// Parses triggers and computes their next run time. Cron expressions are evaluated in the owner's time zone
/// (Cronos handles DST); all returned times are UTC.
/// </summary>
internal static class TaskTriggers
{
    /// <summary>Checks the trigger's spec. <c>after</c> is only checked for its form; the caller checks that the task exists.</summary>
    public static Result Validate(TaskTrigger trigger) => trigger.Kind switch
    {
        TaskTriggerKind.Now => Result.Success(),
        TaskTriggerKind.At => TryParseTime(trigger.Spec, out _) ? Result.Success() : TaskErrors.Invalid($"'{trigger.Spec}' is not an ISO 8601 time."),
        TaskTriggerKind.Cron => TryParseCron(trigger.Spec, out _) ? Result.Success() : TaskErrors.Invalid($"'{trigger.Spec}' is not a five-field cron expression."),
        TaskTriggerKind.Event => EventFilter.Parse(trigger.Spec) is { IsFailure: true } invalid ? invalid.Error : Result.Success(),
        TaskTriggerKind.After => TaskId.TryParse(trigger.Spec, CultureInfo.InvariantCulture, out _) ? Result.Success() : TaskErrors.Invalid($"'{trigger.Spec}' is not a task id."),
        _ => TaskErrors.Invalid($"Unknown trigger kind {trigger.Kind}."),
    };

    /// <summary>
    /// The first run time after <paramref name="now"/>: now for <c>now</c>, the time for <c>at</c>, the next cron
    /// occurrence in <paramref name="zone"/>; null for <c>event</c> and <c>after</c>, which become due by other means.
    /// </summary>
    public static DateTimeOffset? NextRun(TaskTrigger trigger, DateTimeOffset now, TimeZoneInfo zone) => trigger.Kind switch
    {
        TaskTriggerKind.Now => now,
        TaskTriggerKind.At when TryParseTime(trigger.Spec, out DateTimeOffset time) => time,
        TaskTriggerKind.Cron when TryParseCron(trigger.Spec, out CronExpression? cron) => cron.GetNextOccurrence(now, zone)?.ToUniversalTime(),
        _ => null,
    };

    /// <summary>Shortest gap between the next <paramref name="samples"/> occurrences of a valid cron trigger (in UTC, so DST gaps count as they happen).</summary>
    public static TimeSpan ShortestPeriod(TaskTrigger trigger, DateTimeOffset from, int samples)
    {
        if (!TryParseCron(trigger.Spec, out CronExpression? cron))
            return TimeSpan.MaxValue;

        TimeSpan shortest = TimeSpan.MaxValue;
        DateTimeOffset? previous = cron.GetNextOccurrence(from, TimeZoneInfo.Utc);
        for (int i = 0; i < samples && previous is { } last; i++)
        {
            DateTimeOffset? next = cron.GetNextOccurrence(last, TimeZoneInfo.Utc);
            if (next is { } occurrence && occurrence - last < shortest)
                shortest = occurrence - last;
            previous = next;
        }

        return shortest;
    }

    private static bool TryParseTime(string spec, out DateTimeOffset time)
    {
        bool ok = DateTimeOffset.TryParse(spec, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out time);
        time = time.ToUniversalTime();
        return ok;
    }

    private static bool TryParseCron(string spec, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CronExpression? cron) =>
        CronExpression.TryParse(spec, CronFormat.Standard, out cron);
}
