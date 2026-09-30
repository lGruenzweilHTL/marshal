using System.Text.Encodings.Web;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Tasks.Scheduling;

namespace Marshal.Tasks.Tools;

/// <summary>
/// Model-facing task tools. All are safe; the ones that store work for later (<c>create</c>, <c>schedule_wakeup</c>,
/// <c>watch</c>, <c>update</c>) persist, so tainted runs need confirmation, and <c>overrideQuietHoursUntil</c>
/// always does. Tools that start work are hidden while the kill switch is active.
/// </summary>
internal static class TaskTools
{
    private const string OverrideArgument = "overrideQuietHoursUntil";
    private const int ListLimit = 50;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly AgentTaskStatus[] _unfinished = [AgentTaskStatus.Pending, AgentTaskStatus.Running, AgentTaskStatus.Waiting];

    private const string TriggerSchema = """
        {"type":"object","description":"When the task runs. Default: now.","properties":{
          "kind":{"type":"string","enum":["now","at","cron","after"]},
          "time":{"type":"string","description":"For at: local time, e.g. 2026-09-30T07:00."},
          "expression":{"type":"string","description":"For cron: five-field cron expression in local time, e.g. 0 6 * * *."},
          "task":{"type":"string","description":"For after: id or name of the task to run after."}},"required":["kind"]}
        """;

    internal const string DeliverSchema = """
        {"type":"array","description":"Where the result goes. Default: nowhere (visible in the web UI).","items":{"type":"object","properties":{
          "channel":{"type":"string","enum":["voice","push","inbox","silent"]},
          "target":{"type":"string","description":"Voice only: satellite entity id; omit for where the user is."},
          "priority":{"type":"string","enum":["normal","low"]},
          "followUp":{"type":"boolean","description":"Voice only: listen for a reply."},
          "when":{"type":"array","items":{"type":"string"},"description":"Conditions like state(binary_sensor.kitchen_occupancy = on)."},
          "expires":{"type":"string","description":"Local deadline, e.g. 11:00."},
          "fallback":{"type":"string","enum":["voice","push","inbox","silent"]}},"required":["channel"]}}
        """;

    private const string OverrideSchema = """
        {"type":"string","description":"Local time until which this task may notify during quiet hours. Needs the user's confirmation."}
        """;

    public static IReadOnlyList<ITool> Create(TaskToolContext context) =>
    [
        new TaskTool(context, "create", "Creates a background task: a goal the agent works on later, now, at a time, on a schedule or after another task.", $$"""
            {"type":"object","properties":{
              "goal":{"type":"string","description":"What to achieve, with every detail the later run needs."},
              "trigger":{{TriggerSchema}},
              "name":{"type":"string","description":"Optional unique name, e.g. briefing."},
              "template":{"type":"string"},
              "notes":{"type":"string"},
              "deliver":{{DeliverSchema}},
              "{{OverrideArgument}}":{{OverrideSchema}}},"required":["goal"],"additionalProperties":false}
            """, CreateAsync) { Persists = true, Confirming = true, Available = CanStartAsync },
        new TaskTool(context, "schedule_wakeup", "Ends this background run and runs the task again later with its goal and notes (not this conversation). Write everything you need to remember into notes.", """
            {"type":"object","properties":{
              "at":{"type":"string","description":"Local time, e.g. 2026-09-30T07:00."},
              "inMinutes":{"type":"integer","minimum":1},
              "notes":{"type":"string","description":"The task notes for the next run (replace the current notes)."}},"additionalProperties":false}
            """, ScheduleWakeupAsync) { Persists = true, Available = InTaskRunAsync },
        new TaskTool(context, "watch", "Watches for an event (e.g. a person coming home) and then runs a goal.", $$"""
            {"type":"object","properties":{
              "filter":{"type":"object","description":"Event filter: source (ha, printer, mail, ...), optional type, entity glob and payload conditions such as to: home.","properties":{"source":{"type":"string"} },"required":["source"]},
              "goal":{"type":"string"},
              "name":{"type":"string"},
              "deliver":{{DeliverSchema}},
              "{{OverrideArgument}}":{{OverrideSchema}}},"required":["filter","goal"],"additionalProperties":false}
            """, WatchAsync) { Persists = true, Confirming = true, Available = CanStartAsync },
        new TaskTool(context, "update", "Changes a task's goal, schedule, deliveries, template or name.", $$"""
            {"type":"object","properties":{
              "task":{"type":"string","description":"Task id or name."},
              "goal":{"type":"string"},
              "trigger":{{TriggerSchema}},
              "deliver":{{DeliverSchema}},
              "template":{"type":"string"},
              "name":{"type":"string"},
              "{{OverrideArgument}}":{{OverrideSchema}}},"required":["task"],"additionalProperties":false}
            """, UpdateAsync) { Persists = true, Confirming = true, Available = AnyTaskAsync },
        new TaskTool(context, "run_now", "Runs an existing task now, with its normal budget and deliveries.", """
            {"type":"object","properties":{"task":{"type":"string","description":"Task id or name."}},"required":["task"],"additionalProperties":false}
            """, RunNowAsync) { Available = NotPausedAsync },
        new TaskTool(context, "list", "Lists the user's background tasks and watches.", """
            {"type":"object","properties":{"status":{"type":"string","enum":["active","pending","running","waiting","done","failed","cancelled"]}},"additionalProperties":false}
            """, ListAsync),
        new TaskTool(context, "status", "Shows a task's status and its latest result with its age.", """
            {"type":"object","properties":{"task":{"type":"string","description":"Task id or name."}},"required":["task"],"additionalProperties":false}
            """, StatusAsync),
        new TaskTool(context, "cancel", "Cancels a task and its unfinished child tasks.", """
            {"type":"object","properties":{"task":{"type":"string","description":"Task id or name."}},"required":["task"],"additionalProperties":false}
            """, CancelAsync),
        new TaskTool(context, "stop_all", "Kill switch: stops all background tasks and pauses new ones until the user resumes them in the web UI.", """
            {"type":"object","properties":{},"additionalProperties":false}
            """, StopAllAsync),
    ];

    private static async Task<Result<ToolOutput>> CreateAsync(TaskToolContext context, ToolInvocation invocation, CancellationToken ct)
    {
        JsonElement arguments = invocation.Arguments;
        if (TaskArguments.Text(arguments, "goal") is not { } goal)
            return TaskErrors.Invalid("goal is required.");

        TimeZoneInfo zone = await context.Profiles.TimeZoneAsync(invocation.Context.OwnerId, ct);
        Result<TaskTrigger> trigger = await TaskArguments.TriggerAsync(arguments, zone, context.Store, ct);
        if (trigger.IsFailure)
            return trigger.Error;

        return await CreateTaskAsync(context, invocation, zone, goal, trigger.Value, ct);
    }

    private static async Task<Result<ToolOutput>> WatchAsync(TaskToolContext context, ToolInvocation invocation, CancellationToken ct)
    {
        JsonElement arguments = invocation.Arguments;
        if (TaskArguments.Text(arguments, "goal") is not { } goal)
            return TaskErrors.Invalid("goal is required.");
        if (!arguments.TryGetProperty("filter", out JsonElement filter) || filter.ValueKind != JsonValueKind.Object || TaskArguments.Text(filter, "source") is null)
            return TaskErrors.Invalid("filter must be an object with a source.");

        TimeZoneInfo zone = await context.Profiles.TimeZoneAsync(invocation.Context.OwnerId, ct);
        return await CreateTaskAsync(context, invocation, zone, goal, TaskTrigger.Event(filter.GetRawText()), ct);
    }

    private static async Task<Result<ToolOutput>> CreateTaskAsync(TaskToolContext context, ToolInvocation invocation, TimeZoneInfo zone, string goal, TaskTrigger trigger, CancellationToken ct)
    {
        JsonElement arguments = invocation.Arguments;
        MarshalExecutionContext run = invocation.Context;
        Result<IReadOnlyList<TaskDelivery>> deliveries = TaskArguments.Deliveries(arguments);
        if (deliveries.IsFailure)
            return deliveries.Error;
        Result<DateTimeOffset?> overrideUntil = Override(arguments, zone);
        if (overrideUntil.IsFailure)
            return overrideUntil.Error;

        Result<TaskRecord> created = await context.Store.CreateAsync(new NewTask
        {
            Goal = goal,
            Trigger = trigger,
            Name = TaskArguments.Text(arguments, "name"),
            Template = TaskArguments.Text(arguments, "template"),
            Notes = TaskArguments.Text(arguments, "notes"),
            Deliveries = deliveries.Value,
            OverrideQuietHoursUntil = overrideUntil.Value,
            // Inside a task run the new task is its child; the origin satellite and project carry over.
            ParentId = run.TaskId,
            CreatedBy = run.TaskId is null ? TaskCreator.User : TaskCreator.Task,
            OriginSatelliteId = run.SatelliteId,
            ProjectId = run.ProjectId,
        }, ct);
        if (created.IsFailure)
            return created.Error;

        context.Scheduler.Wake();
        return Output(TaskViews.Summary(created.Value, zone, context.Time.GetUtcNow()), created.Value.Id.ToString());
    }

    private static async Task<Result<ToolOutput>> ScheduleWakeupAsync(TaskToolContext context, ToolInvocation invocation, CancellationToken ct)
    {
        JsonElement arguments = invocation.Arguments;
        if (invocation.Context.TaskId is not TaskId id || context.Runs.Find(id) is not { } state)
            return TaskErrors.Invalid("schedule_wakeup only works inside a background task run.");
        Result<TaskRecord> task = await context.Store.GetAsync(id, ct);
        if (task.IsFailure)
            return task.Error;

        TimeZoneInfo zone = await context.Profiles.TimeZoneAsync(invocation.Context.OwnerId, ct);
        DateTimeOffset wakeAt;
        if (TaskArguments.Text(arguments, "at") is { } at)
        {
            Result<DateTimeOffset> parsed = TaskArguments.Time(at, zone);
            if (parsed.IsFailure)
                return parsed.Error;
            wakeAt = parsed.Value;
        }
        else if (arguments.TryGetProperty("inMinutes", out JsonElement minutes) && minutes.TryGetInt32(out int delay) && delay > 0)
        {
            wakeAt = context.Time.GetUtcNow().AddMinutes(delay);
        }
        else
        {
            return TaskErrors.Invalid("Give either at or inMinutes.");
        }

        Result allowed = context.Budgets.CheckWakeup(task.Value, wakeAt);
        if (allowed.IsFailure)
            return allowed.Error;

        state.RequestWait(wakeAt, TaskArguments.Text(arguments, "notes"));
        return Output(new { wakeAt = TaskViews.Local(wakeAt, zone), next = "End this run now with a short status." }, id.ToString());
    }

    private static async Task<Result<ToolOutput>> UpdateAsync(TaskToolContext context, ToolInvocation invocation, CancellationToken ct)
    {
        JsonElement arguments = invocation.Arguments;
        Result<TaskRecord> found = await FindAsync(context, arguments, ct);
        if (found.IsFailure)
            return found.Error;

        TaskRecord task = found.Value;
        TimeZoneInfo zone = await context.Profiles.TimeZoneAsync(task.OwnerId, ct);
        TaskRecord updated = task with
        {
            Goal = TaskArguments.Text(arguments, "goal") ?? task.Goal,
            Template = TaskArguments.Text(arguments, "template") ?? task.Template,
            Name = TaskArguments.Text(arguments, "name") ?? task.Name,
        };

        if (TaskArguments.Has(arguments, "trigger"))
        {
            Result<TaskTrigger> trigger = await TaskArguments.TriggerAsync(arguments, zone, context.Store, ct);
            if (trigger.IsFailure)
                return trigger.Error;
            updated = updated with { Trigger = trigger.Value };
            // A new schedule takes effect now; a running task picks it up when its run ends.
            if (task.Status != AgentTaskStatus.Running)
            {
                updated = updated with
                {
                    Status = task.Status == AgentTaskStatus.Waiting ? AgentTaskStatus.Waiting : AgentTaskStatus.Pending,
                    StatusReason = null,
                    NextRunAt = TaskTriggers.NextRun(trigger.Value, context.Time.GetUtcNow(), zone),
                };
            }
        }

        if (TaskArguments.Has(arguments, "deliver"))
        {
            Result<IReadOnlyList<TaskDelivery>> deliveries = TaskArguments.Deliveries(arguments);
            if (deliveries.IsFailure)
                return deliveries.Error;
            updated = updated with { Deliveries = deliveries.Value };
        }

        Result<DateTimeOffset?> overrideUntil = Override(arguments, zone);
        if (overrideUntil.IsFailure)
            return overrideUntil.Error;
        if (overrideUntil.Value is not null)
            updated = updated with { OverrideQuietHoursUntil = overrideUntil.Value };

        Result<TaskRecord> saved = await context.Store.UpdateAsync(updated, ct);
        if (saved.IsFailure)
            return saved.Error;

        context.Scheduler.Wake();
        return Output(TaskViews.Summary(saved.Value, zone, context.Time.GetUtcNow()), task.Id.ToString());
    }

    private static async Task<Result<ToolOutput>> RunNowAsync(TaskToolContext context, ToolInvocation invocation, CancellationToken ct)
    {
        Result<TaskRecord> found = await FindAsync(context, invocation.Arguments, ct);
        if (found.IsFailure)
            return found.Error;

        TaskRecord task = found.Value;
        if (task.Status == AgentTaskStatus.Running)
            return new Error("tasks.running", "The task is running right now.", ErrorType.Conflict);

        Result<TaskRecord> saved = await context.Store.UpdateAsync(task with
        {
            Status = task.Status == AgentTaskStatus.Waiting ? AgentTaskStatus.Waiting : AgentTaskStatus.Pending,
            StatusReason = null,
            NextRunAt = context.Time.GetUtcNow(),
        }, ct);
        if (saved.IsFailure)
            return saved.Error;

        context.Scheduler.Wake();
        return Output(new { started = true, task = task.Name ?? task.Id.ToString() }, task.Id.ToString());
    }

    private static async Task<Result<ToolOutput>> ListAsync(TaskToolContext context, ToolInvocation invocation, CancellationToken ct)
    {
        IReadOnlyCollection<AgentTaskStatus>? statuses = TaskArguments.Text(invocation.Arguments, "status") switch
        {
            null => null,
            "active" => _unfinished,
            string named when Enum.TryParse(named, ignoreCase: true, out AgentTaskStatus status) => [status],
            _ => null,
        };
        IReadOnlyList<TaskRecord> tasks = await context.Store.ListAsync(new TaskFilter(statuses), ct);
        TimeZoneInfo zone = await context.Profiles.TimeZoneAsync(invocation.Context.OwnerId, ct);
        DateTimeOffset now = context.Time.GetUtcNow();
        return Output(tasks.TakeLast(ListLimit).Select(task => TaskViews.Summary(task, zone, now)), tasks.Count > ListLimit ? $"{tasks.Count} tasks" : null);
    }

    private static async Task<Result<ToolOutput>> StatusAsync(TaskToolContext context, ToolInvocation invocation, CancellationToken ct)
    {
        Result<TaskRecord> found = await FindAsync(context, invocation.Arguments, ct);
        if (found.IsFailure)
            return found.Error;

        TimeZoneInfo zone = await context.Profiles.TimeZoneAsync(found.Value.OwnerId, ct);
        return Output(TaskViews.Detail(found.Value, zone, context.Time.GetUtcNow()), found.Value.Id.ToString());
    }

    private static async Task<Result<ToolOutput>> CancelAsync(TaskToolContext context, ToolInvocation invocation, CancellationToken ct)
    {
        Result<TaskRecord> found = await FindAsync(context, invocation.Arguments, ct);
        if (found.IsFailure)
            return found.Error;

        Result<int> cancelled = await context.Control.CancelAsync(found.Value.Id, ct);
        return cancelled.IsSuccess ? Output(new { cancelled = cancelled.Value }, found.Value.Id.ToString()) : cancelled.Error;
    }

    private static async Task<Result<ToolOutput>> StopAllAsync(TaskToolContext context, ToolInvocation invocation, CancellationToken ct)
    {
        int cancelled = await context.KillSwitch.ActivateAsync(ct);
        return Output(new { cancelled, paused = true, resume = "New background tasks stay paused until the user resumes them in the web UI." }, null);
    }

    private static async ValueTask<bool> NotPausedAsync(TaskToolContext context, MarshalExecutionContext run, CancellationToken ct) =>
        !await context.KillSwitch.IsActiveAsync(ct);

    // Inside a task run, a new task is a child: hidden once the depth or fan-out limit is reached.
    private static async ValueTask<bool> CanStartAsync(TaskToolContext context, MarshalExecutionContext run, CancellationToken ct)
    {
        if (!await NotPausedAsync(context, run, ct))
            return false;
        if (run.TaskId is not TaskId id)
            return true;

        Result<TaskRecord> task = await context.Store.GetAsync(id, ct);
        return task.IsSuccess
            && task.Value.Depth < context.Options.Value.MaxDepth
            && (await context.Store.ListAsync(new TaskFilter(_unfinished, id), ct)).Count < context.Options.Value.MaxActiveChildren;
    }

    private static async ValueTask<bool> InTaskRunAsync(TaskToolContext context, MarshalExecutionContext run, CancellationToken ct) =>
        run.TaskId is TaskId id && context.Runs.Find(id) is not null && await NotPausedAsync(context, run, ct);

    private static async ValueTask<bool> AnyTaskAsync(TaskToolContext context, MarshalExecutionContext run, CancellationToken ct) =>
        (await context.Store.ListAsync(new TaskFilter(), ct)).Count > 0;

    private static Task<Result<TaskRecord>> FindAsync(TaskToolContext context, JsonElement arguments, CancellationToken ct) =>
        TaskArguments.Text(arguments, "task") is { } reference
            ? context.Store.FindAsync(reference, ct)
            : Task.FromResult<Result<TaskRecord>>(TaskErrors.Invalid("task is required (id or name)."));

    private static Result<DateTimeOffset?> Override(JsonElement arguments, TimeZoneInfo zone)
    {
        if (TaskArguments.Text(arguments, OverrideArgument) is not { } text)
            return (DateTimeOffset?)null;
        Result<DateTimeOffset> until = TaskArguments.Time(text, zone);
        return until.IsFailure ? until.Error : until.Value;
    }

    private static ToolOutput Output(object value, string? reference) => new(JsonSerializer.Serialize(value, _json), reference);

    private sealed class TaskTool(
        TaskToolContext context,
        string name,
        string description,
        string schema,
        Func<TaskToolContext, ToolInvocation, CancellationToken, Task<Result<ToolOutput>>> run) : ITool
    {
        private readonly JsonElement _schema = JsonDocument.Parse(schema).RootElement.Clone();

        public bool Persists { get; init; }

        public bool Confirming { get; init; }

        public Func<TaskToolContext, MarshalExecutionContext, CancellationToken, ValueTask<bool>>? Available { get; init; }

        public ToolDescriptor Descriptor => field ??= new ToolDescriptor
        {
            Id = new ToolId("tasks", name),
            Description = description,
            ParametersSchema = _schema,
            DefaultTier = RiskTier.Safe,
            Persists = Persists,
            ConfirmingArguments = Confirming
                ? new Dictionary<string, ConfirmationReasonCode> { [OverrideArgument] = ConfirmationReasonCode.QuietHoursOverride }
                : new Dictionary<string, ConfirmationReasonCode>(),
        };

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext executionContext, CancellationToken cancellationToken) =>
            Available is null ? ValueTask.FromResult(true) : Available(context, executionContext, cancellationToken);

        // Only the declared arguments are accepted: owner, budget and origin satellite can never be passed.
        public Result Validate(JsonElement arguments)
        {
            if (arguments.ValueKind != JsonValueKind.Object)
                return TaskErrors.Invalid("Arguments must be an object.");
            JsonElement properties = _schema.GetProperty("properties");
            foreach (JsonProperty argument in arguments.EnumerateObject())
            {
                if (!properties.TryGetProperty(argument.Name, out _))
                    return TaskErrors.Invalid($"Unknown argument '{argument.Name}'.");
            }

            return Result.Success();
        }

        public string Summarize(JsonElement arguments, string language)
        {
            string goal = TaskArguments.Text(arguments, "goal") ?? TaskArguments.Text(arguments, "task") ?? name;
            return TaskArguments.Text(arguments, OverrideArgument) is { } until
                ? (language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? $"{goal} (auch während der Ruhezeit bis {until})" : $"{goal} (also during quiet hours until {until})")
                : goal;
        }

        public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result valid = Validate(invocation.Arguments);
            return valid.IsFailure ? Task.FromResult<Result<ToolOutput>>(valid.Error) : run(context, invocation, cancellationToken);
        }
    }
}
