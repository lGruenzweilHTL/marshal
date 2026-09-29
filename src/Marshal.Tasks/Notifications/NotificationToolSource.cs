using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Tasks.Tools;

namespace Marshal.Tasks.Notifications;

/// <summary>
/// The built-in <c>notifications.*</c> tools: <c>notify</c> (priority <c>normal</c> or <c>low</c> only; inside a task
/// run the task is the source and its quiet-hours override applies), <c>inbox_list</c> (untrusted output: items may
/// carry third-party text) and <c>inbox_consume</c>.
/// </summary>
internal sealed class NotificationToolSource(
    INotificationSender sender,
    INotificationInbox inbox,
    ITaskStore tasks,
    IUserProfileProvider profiles) : IToolSource
{
    private const int ListLimit = 50;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public string Name => "notifications";

    public bool IsBuiltIn => true;

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ITool>>(
    [
        new NotificationTool("notify", "Notifies the user now: spoken where they are (or on a satellite), pushed or put in the inbox. Low priority goes to the inbox.", """
            {"type":"object","properties":{
              "message":{"type":"string","description":"Short, speakable text."},
              "priority":{"type":"string","enum":["normal","low"]},
              "deliver":{"type":"object","properties":{
                "channel":{"type":"string","enum":["voice","push","inbox"]},
                "target":{"type":"string","description":"Voice only: satellite entity id; omit for where the user is."}}}},
              "required":["message"],"additionalProperties":false}
            """, false, NotifyAsync),
        new NotificationTool("inbox_list", "Lists unread items in the user's notification inbox, newest first.", """
            {"type":"object","properties":{},"additionalProperties":false}
            """, true, ListAsync),
        new NotificationTool("inbox_consume", "Marks inbox items as handled so they are not listed again (e.g. after a briefing covered them).", """
            {"type":"object","properties":{"ids":{"type":"array","items":{"type":"string"}}},"required":["ids"],"additionalProperties":false}
            """, false, ConsumeAsync),
    ]);

    private async Task<Result<ToolOutput>> NotifyAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        JsonElement arguments = invocation.Arguments;
        if (TaskArguments.Text(arguments, "message") is not { } message)
            return Invalid("message is required.");

        NotificationPriority priority = NotificationPriority.Normal;
        if (TaskArguments.Text(arguments, "priority") is { } named)
        {
            if (named is not ("normal" or "low"))
                return Invalid("priority is normal or low; only system sources send critical notifications.");
            priority = named == "low" ? NotificationPriority.Low : NotificationPriority.Normal;
        }

        DeliveryChannel? channel = null;
        string? target = null;
        if (arguments.TryGetProperty("deliver", out JsonElement deliver) && deliver.ValueKind == JsonValueKind.Object)
        {
            switch (TaskArguments.Text(deliver, "channel"))
            {
                case null:
                    break;
                case "voice":
                    channel = DeliveryChannel.Voice;
                    break;
                case "push":
                    channel = DeliveryChannel.Push;
                    break;
                case "inbox":
                    channel = DeliveryChannel.Inbox;
                    break;
                default:
                    return Invalid("deliver.channel is voice, push or inbox.");
            }

            target = TaskArguments.Text(deliver, "target");
            if (target is not null && channel != DeliveryChannel.Voice)
                return Invalid("Only voice deliveries have a target.");
        }

        if (channel == DeliveryChannel.Voice && priority == NotificationPriority.Low)
            return Invalid("Low-priority notifications never use voice.");

        MarshalExecutionContext run = invocation.Context;
        TaskRecord? task = run.TaskId is TaskId id && await tasks.GetAsync(id, cancellationToken) is { IsSuccess: true } found ? found.Value : null;
        Result<Guid> sent = await sender.SendAsync(new NotificationRequest
        {
            Owner = run.OwnerId,
            Source = task is null ? "agent" : $"task:{task.Id}",
            Priority = priority,
            Text = message,
            Channel = channel,
            Target = target,
            TaskId = task?.Id,
            Link = task is null ? null : $"tasks/{task.Id}",
            OverrideQuietHoursUntil = task?.OverrideQuietHoursUntil,
        }, cancellationToken);
        return sent.IsFailure ? sent.Error : new ToolOutput(JsonSerializer.Serialize(new { sent = true }, _json), sent.Value.ToString());
    }

    private async Task<Result<ToolOutput>> ListAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        IReadOnlyList<InboxItem> items = await inbox.ListAsync(ListLimit, cancellationToken);
        TimeZoneInfo zone = await profiles.TimeZoneAsync(invocation.Context.OwnerId, cancellationToken);
        return new ToolOutput(JsonSerializer.Serialize(items.Select(item => new
        {
            id = item.Id.ToString(),
            source = item.Source,
            priority = item.Priority.ToString().ToLowerInvariant(),
            text = item.Text,
            at = TaskViews.Local(item.CreatedAt, zone),
        }), _json));
    }

    private async Task<Result<ToolOutput>> ConsumeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (!invocation.Arguments.TryGetProperty("ids", out JsonElement ids) || ids.ValueKind != JsonValueKind.Array)
            return Invalid("ids must be a list of inbox item ids.");

        var parsed = new List<Guid>();
        foreach (JsonElement id in ids.EnumerateArray())
        {
            if (id.ValueKind != JsonValueKind.String || !Guid.TryParse(id.GetString(), CultureInfo.InvariantCulture, out Guid value))
                return Invalid($"'{id}' is not an inbox item id.");
            parsed.Add(value);
        }

        int consumed = await inbox.ConsumeAsync(parsed, cancellationToken);
        return new ToolOutput(JsonSerializer.Serialize(new { consumed }, _json));
    }

    private static Error Invalid(string message) => new("notifications.invalid", message, ErrorType.Validation);

    private sealed class NotificationTool(
        string name,
        string description,
        string schema,
        bool untrusted,
        Func<ToolInvocation, CancellationToken, Task<Result<ToolOutput>>> run) : ITool
    {
        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("notifications", name),
            Description = description,
            ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
            DefaultTier = RiskTier.Safe,
            UntrustedOutput = untrusted,
        };

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext executionContext, CancellationToken cancellationToken) => ValueTask.FromResult(true);

        public Result Validate(JsonElement arguments) =>
            arguments.ValueKind == JsonValueKind.Object ? Result.Success() : Invalid("Arguments must be an object.");

        public string Summarize(JsonElement arguments, string language) => TaskArguments.Text(arguments, "message") ?? name;

        public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result valid = Validate(invocation.Arguments);
            return valid.IsFailure ? Task.FromResult<Result<ToolOutput>>(valid.Error) : run(invocation, cancellationToken);
        }
    }
}
