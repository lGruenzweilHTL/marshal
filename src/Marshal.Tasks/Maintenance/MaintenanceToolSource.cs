using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Tools;
using Marshal.Tasks.Tools;

namespace Marshal.Tasks.Maintenance;

/// <summary>
/// The built-in <c>maintenance.*</c> tools over <c>maintenance.items</c>: <c>status</c> (usage, interval, state and
/// last done per item) and <c>done</c> (sets the baseline to the current counter value; persists, so a tainted run
/// needs a confirmation). Without configured items there are no tools.
/// </summary>
internal sealed class MaintenanceToolSource(MaintenanceTracker tracker) : IToolSource
{
    public const string Namespace = "maintenance";

    public string Name => Namespace;

    public bool IsBuiltIn => true;

    public IReadOnlyList<ToolNamespace> Namespaces { get; } = [new(Namespace, "Usage-based maintenance: what is due (print hours, cycles) and marking it done")];

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<ITool>>(tracker.Items.Count == 0
        ? []
        : [
            new MaintenanceTool("status", $"Maintenance items by usage with their state (ok, soon, due), usage, interval and when they were last done. Items: {Items()}.", """
                {"type":"object","properties":{"item":{"type":"string","description":"One item id; omit for all."}},"additionalProperties":false}
                """, persists: false, StatusAsync),
            new MaintenanceTool("done", $"Marks a maintenance item as done now (e.g. the user changed the nozzle): its usage starts again from zero. Items: {Items()}.", """
                {"type":"object","properties":{"item":{"type":"string","description":"Item id."}},"required":["item"],"additionalProperties":false}
                """, persists: true, DoneAsync),
        ]);

    private string Items() => string.Join(", ", tracker.Items.Select(item => $"{item.Key} ({item.Value.Name})"));

    private async Task<Result<ToolOutput>> StatusAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        string? item = TaskArguments.Text(invocation.Arguments, "item");
        if (item is not null && !tracker.Items.ContainsKey(item))
            return MaintenanceErrors.UnknownItem(item, tracker.Items.Keys);

        IReadOnlyList<MaintenanceItemStatus> statuses = item is null
            ? await tracker.StatusAsync(cancellationToken)
            : [(await tracker.CheckAsync(item, cancellationToken))!];
        return new ToolOutput(JsonSerializer.Serialize(statuses, MaintenanceJson.Options));
    }

    private async Task<Result<ToolOutput>> DoneAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        if (TaskArguments.Text(invocation.Arguments, "item") is not { } item)
            return new Error("maintenance.invalid_arguments", "item is required.", ErrorType.Validation);

        Result<MaintenanceItemStatus> done = await tracker.DoneAsync(item, cancellationToken);
        return done.IsSuccess ? new ToolOutput(JsonSerializer.Serialize(done.Value, MaintenanceJson.Options)) : done.Error;
    }

    private sealed class MaintenanceTool(
        string name,
        string description,
        string schema,
        bool persists,
        Func<ToolInvocation, CancellationToken, Task<Result<ToolOutput>>> run) : ITool
    {
        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId(Namespace, name),
            Description = description,
            ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
            DefaultTier = RiskTier.Safe,
            Persists = persists,
        };

        public Result Validate(JsonElement arguments) =>
            arguments.ValueKind == JsonValueKind.Object ? Result.Success() : new Error("maintenance.invalid_arguments", "Arguments must be an object.", ErrorType.Validation);

        public string Summarize(JsonElement arguments, string language)
        {
            string item = TaskArguments.Text(arguments, "item") ?? "";
            return name == "done"
                ? language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? $"Wartung {item} als erledigt markieren" : $"Mark maintenance {item} as done"
                : language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "Wartungsstand abfragen" : "Check maintenance";
        }

        public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result valid = Validate(invocation.Arguments);
            return valid.IsFailure ? Task.FromResult<Result<ToolOutput>>(valid.Error) : run(invocation, cancellationToken);
        }
    }
}
