using System.Text.Encodings.Web;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Tasks.Tools;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Executors;

/// <summary>
/// <c>tasks.run_executor(executor, spec, onDoneGoal)</c> (confirm): starts long external work, e.g. a Claude Code
/// session, and creates the task that runs <c>onDoneGoal</c> with the outcome. Hidden when no executor is configured,
/// while the kill switch is active and when the calling task cannot have another child. The result is delivered
/// like a moved turn's (voice to the satellite, push, or the inbox) unless <c>deliver</c> is given.
/// </summary>
public sealed class RunExecutorTool(ExecutorService executors, ITaskStore tasks, IKillSwitch killSwitch, IOptions<TaskOptions> options) : ITool
{
    private const int MaxSummarySpec = 300;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };
    private static readonly AgentTaskStatus[] _unfinished = [AgentTaskStatus.Pending, AgentTaskStatus.Running, AgentTaskStatus.Waiting];
    private static readonly HashSet<string> _arguments = new(StringComparer.Ordinal) { "executor", "spec", "onDoneGoal", "deliver" };

    public ToolDescriptor Descriptor => field ??= new ToolDescriptor
    {
        Id = new ToolId("tasks", "run_executor"),
        Description = "Starts long external work in an executor and runs onDoneGoal when it finishes. Executors: "
            + string.Join(" ", executors.Executors.Select(executor => $"{executor.Name}: {executor.Description}")),
        ParametersSchema = JsonDocument.Parse($$"""
            {"type":"object","properties":{
              "executor":{"type":"string","enum":{{JsonSerializer.Serialize(executors.Executors.Select(executor => executor.Name))}}},
              "spec":{"type":"object","description":"What the executor should do (see the executor's spec)."},
              "onDoneGoal":{"type":"string","description":"What to do with the outcome, e.g. tell the user the change is ready for review."},
              "deliver":{{TaskTools.DeliverSchema}}},"required":["executor","spec","onDoneGoal"],"additionalProperties":false}
            """).RootElement.Clone(),
        DefaultTier = RiskTier.Confirm,
        Persists = true,
    };

    public async ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        if (executors.Executors.Count == 0 || await killSwitch.IsActiveAsync(cancellationToken))
            return false;
        if (context.TaskId is not TaskId id)
            return true;

        Result<TaskRecord> task = await tasks.GetAsync(id, cancellationToken);
        return task.IsSuccess
            && task.Value.Depth < options.Value.MaxDepth
            && (await tasks.ListAsync(new TaskFilter(_unfinished, id), cancellationToken)).Count < options.Value.MaxActiveChildren;
    }

    // Only the declared arguments; the executor checks its own spec before anything is confirmed.
    public Result Validate(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object)
            return TaskErrors.Invalid("Arguments must be an object.");
        foreach (JsonProperty argument in arguments.EnumerateObject())
        {
            if (!_arguments.Contains(argument.Name))
                return TaskErrors.Invalid($"Unknown argument '{argument.Name}'.");
        }

        if (TaskArguments.Text(arguments, "executor") is not { } name || TaskArguments.Text(arguments, "onDoneGoal") is null)
            return TaskErrors.Invalid("executor and onDoneGoal are required.");
        if (!arguments.TryGetProperty("spec", out JsonElement spec))
            return TaskErrors.Invalid("spec is required.");
        return executors.Executors.FirstOrDefault(executor => string.Equals(executor.Name, name, StringComparison.OrdinalIgnoreCase)) is { } found
            ? found.ValidateSpec(spec)
            : new Error("executor.unknown", $"There is no executor '{name}'.", ErrorType.Validation);
    }

    public string Summarize(JsonElement arguments, string language)
    {
        string spec = arguments.TryGetProperty("spec", out JsonElement value) ? JsonSerializer.Serialize(value, _json) : "{}";
        if (spec.Length > MaxSummarySpec)
            spec = spec[..MaxSummarySpec] + "…";
        string executor = TaskArguments.Text(arguments, "executor") ?? "?";
        return language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? $"{executor} starten: {spec}" : $"Start {executor}: {spec}";
    }

    public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        JsonElement arguments = invocation.Arguments;
        Result valid = Validate(arguments);
        if (valid.IsFailure)
            return valid.Error;
        Result<IReadOnlyList<TaskDelivery>> deliveries = TaskArguments.Deliveries(arguments);
        if (deliveries.IsFailure)
            return deliveries.Error;

        MarshalExecutionContext run = invocation.Context;
        var onDone = new NewTask
        {
            Goal = TaskArguments.Text(arguments, "onDoneGoal")!,
            Trigger = TaskTrigger.Now(),
            ParentId = run.TaskId,
            CreatedBy = run.TaskId is null ? TaskCreator.User : TaskCreator.Task,
            Template = run.Template,
            ProjectId = run.ProjectId,
            OriginSatelliteId = run.SatelliteId,
            Deliveries = TaskArguments.Has(arguments, "deliver") ? deliveries.Value : [DefaultDelivery(run)],
        };

        Result<StartedSession> started = await executors.StartAsync(TaskArguments.Text(arguments, "executor")!, arguments.GetProperty("spec"), onDone, cancellationToken);
        if (started.IsFailure)
            return started.Error;

        string output = JsonSerializer.Serialize(new
        {
            session = started.Value.SessionId,
            task = started.Value.TaskId.ToString(),
            status = "started",
            next = "The work runs in the background; tell the user briefly that it started. The outcome arrives later.",
        }, _json);
        return new ToolOutput(output, started.Value.TaskId.ToString());
    }

    private static TaskDelivery DefaultDelivery(MarshalExecutionContext context) => context switch
    {
        { InputMode: InputMode.Voice, SatelliteId: { } satellite } => new TaskDelivery(DeliveryChannel.Voice, satellite),
        { InputMode: InputMode.Voice } => new TaskDelivery(DeliveryChannel.Push),
        _ => new TaskDelivery(DeliveryChannel.Inbox),
    };
}
