using System.Globalization;

namespace Marshal.Abstractions.Tasks;

/// <summary>
/// What makes a task due. <paramref name="Spec"/> depends on the kind: empty (<c>now</c>), a UTC ISO 8601 time
/// (<c>at</c>), a five-field cron expression evaluated in the owner's time zone (<c>cron</c>), an event filter JSON
/// object (<c>event</c>) or a task id (<c>after</c>).
/// </summary>
public sealed record TaskTrigger(TaskTriggerKind Kind, string Spec)
{
    public static TaskTrigger Now() => new(TaskTriggerKind.Now, "");

    public static TaskTrigger At(DateTimeOffset time) =>
        new(TaskTriggerKind.At, time.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));

    public static TaskTrigger Cron(string expression) => new(TaskTriggerKind.Cron, expression.Trim());

    public static TaskTrigger Event(string filterJson) => new(TaskTriggerKind.Event, filterJson);

    public static TaskTrigger After(TaskId task) => new(TaskTriggerKind.After, task.ToString());
}
