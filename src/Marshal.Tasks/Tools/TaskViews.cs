using System.Globalization;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Tools;

/// <summary>
/// How tasks look to the model: local times, lowercase names, the result with its age.
/// </summary>
internal static class TaskViews
{
    public static object Summary(TaskRecord task, TimeZoneInfo zone, DateTimeOffset now) => new
    {
        id = task.Id.ToString(),
        name = task.Name,
        goal = task.Goal.Length > 200 ? task.Goal[..200] + "…" : task.Goal,
        status = Name(task.Status),
        reason = task.StatusReason,
        trigger = Describe(task.Trigger),
        once = Once(task.Trigger),
        nextRun = task.NextRunAt is { } next ? Local(next, zone) : null,
        resultAgeMinutes = task.ResultAt is { } at ? (int?)(now - at).TotalMinutes : null,
    };

    public static object Detail(TaskRecord task, TimeZoneInfo zone, DateTimeOffset now) => new
    {
        id = task.Id.ToString(),
        name = task.Name,
        goal = task.Goal,
        status = Name(task.Status),
        reason = task.StatusReason,
        trigger = Describe(task.Trigger),
        once = Once(task.Trigger),
        nextRun = task.NextRunAt is { } next ? Local(next, zone) : null,
        notes = task.Notes,
        result = task.Result is { } result
            ? new { speech = result.Speech, body = result.Body, at = task.ResultAt is { } at ? Local(at, zone) : null, ageMinutes = task.ResultAt is { } resultAt ? (int)(now - resultAt).TotalMinutes : 0 }
            : null,
        spent = new { task.Spent.Cost, task.Spent.Runs },
    };

    public static string Local(DateTimeOffset time, TimeZoneInfo zone) =>
        TimeZoneInfo.ConvertTime(time, zone).ToString("yyyy-MM-dd'T'HH:mm:sszzz", CultureInfo.InvariantCulture);

    // Watches say whether they end after their first event; other triggers leave it out.
    private static bool? Once(TaskTrigger trigger) => trigger.Kind == TaskTriggerKind.Event ? trigger.Once : null;

    private static string Name(AgentTaskStatus status) => status.ToString().ToLowerInvariant();

    private static string Describe(TaskTrigger trigger) => trigger.Kind switch
    {
        TaskTriggerKind.Now => "now",
        TaskTriggerKind.At => $"at {trigger.Spec}",
        TaskTriggerKind.Cron => $"cron {trigger.Spec}",
        TaskTriggerKind.Event => $"event {trigger.Spec}",
        _ => $"after {trigger.Spec}",
    };
}
