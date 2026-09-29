using System.Globalization;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Tools;

/// <summary>
/// Parses task tool arguments. Times without an offset are in the owner's time zone.
/// </summary>
internal static class TaskArguments
{
    public static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? text.Trim()
            : null;

    public static bool Has(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind != JsonValueKind.Null;

    public static Result<DateTimeOffset> Time(string text, TimeZoneInfo zone)
    {
        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTimeOffset withOffset) && HasOffset(text))
            return withOffset.ToUniversalTime();
        if (DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime local))
            return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
        return TaskErrors.Invalid($"'{text}' is not a time like 2026-09-30T07:00.");
    }

    /// <summary>Parses <c>{"kind": "now" | "at" | "cron" | "after", ...}</c>; <c>after</c> names a task by id or name.</summary>
    public static async Task<Result<TaskTrigger>> TriggerAsync(JsonElement arguments, TimeZoneInfo zone, ITaskStore store, CancellationToken cancellationToken)
    {
        if (!Has(arguments, "trigger"))
            return TaskTrigger.Now();

        JsonElement trigger = arguments.GetProperty("trigger");
        if (trigger.ValueKind != JsonValueKind.Object)
            return TaskErrors.Invalid("trigger must be an object with a kind.");

        switch (Text(trigger, "kind"))
        {
            case "now":
                return TaskTrigger.Now();
            case "at":
                if (Text(trigger, "time") is not { } time)
                    return TaskErrors.Invalid("An 'at' trigger needs a time.");
                Result<DateTimeOffset> at = Time(time, zone);
                return at.IsFailure ? at.Error : TaskTrigger.At(at.Value);
            case "cron":
                return Text(trigger, "expression") is { } expression ? TaskTrigger.Cron(expression) : TaskErrors.Invalid("A 'cron' trigger needs an expression.");
            case "after":
                if (Text(trigger, "task") is not { } reference)
                    return TaskErrors.Invalid("An 'after' trigger needs a task.");
                Result<TaskRecord> other = await store.FindAsync(reference, cancellationToken);
                return other.IsFailure ? TaskErrors.Invalid($"There is no task '{reference}' to run after.") : TaskTrigger.After(other.Value.Id);
            default:
                return TaskErrors.Invalid("trigger.kind must be now, at, cron or after.");
        }
    }

    /// <summary>Parses <c>deliver</c>; channel and priority names only (the delivery rules are checked by the store's caller).</summary>
    public static Result<IReadOnlyList<TaskDelivery>> Deliveries(JsonElement arguments)
    {
        if (!Has(arguments, "deliver"))
            return Result.Success<IReadOnlyList<TaskDelivery>>([]);

        JsonElement list = arguments.GetProperty("deliver");
        if (list.ValueKind != JsonValueKind.Array)
            return TaskErrors.Invalid("deliver must be a list.");

        var deliveries = new List<TaskDelivery>();
        foreach (JsonElement item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || Channel(Text(item, "channel")) is not { } channel)
                return TaskErrors.Invalid("Each delivery needs a channel: voice, push, inbox or silent.");

            NotificationPriority priority = NotificationPriority.Normal;
            if (Text(item, "priority") is { } named)
            {
                if (named is not ("normal" or "low"))
                    return TaskErrors.Invalid("A delivery's priority is normal or low.");
                priority = named == "low" ? NotificationPriority.Low : NotificationPriority.Normal;
            }

            DeliveryChannel? fallback = null;
            if (Text(item, "fallback") is { } fallbackName)
            {
                fallback = Channel(fallbackName);
                if (fallback is null)
                    return TaskErrors.Invalid("A delivery's fallback is voice, push, inbox or silent.");
            }

            List<string>? when = null;
            if (Has(item, "when"))
            {
                if (item.GetProperty("when").ValueKind != JsonValueKind.Array)
                    return TaskErrors.Invalid("A delivery's 'when' is a list of state conditions.");
                when = [.. item.GetProperty("when").EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!.Trim())];
            }

            bool followUp = item.TryGetProperty("followUp", out JsonElement follow) && follow.ValueKind == JsonValueKind.True;
            deliveries.Add(new TaskDelivery(channel, Text(item, "target"), priority, followUp, when, Text(item, "expires"), fallback));
        }

        return deliveries;
    }

    private static DeliveryChannel? Channel(string? name) => name switch
    {
        "voice" => DeliveryChannel.Voice,
        "push" => DeliveryChannel.Push,
        "inbox" => DeliveryChannel.Inbox,
        "silent" => DeliveryChannel.Silent,
        _ => null,
    };

    // "2026-09-30T07:00+02:00", "...Z": an explicit offset; "2026-09-30T07:00": local time.
    private static bool HasOffset(string text)
    {
        int time = text.IndexOfAny(['T', ' ']);
        if (time < 0)
            return false;
        string tail = text[time..].TrimEnd();
        return tail.EndsWith('Z') || tail.Contains('+', StringComparison.Ordinal) || tail.Contains('-', StringComparison.Ordinal);
    }
}
