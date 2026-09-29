using Marshal.Abstractions.Memory;

namespace Marshal.Abstractions.Tasks;

/// <summary>
/// A task to create for the current owner. Depth follows from the parent.
/// </summary>
public sealed record NewTask
{
    public required string Goal { get; init; }

    public required TaskTrigger Trigger { get; init; }

    public TaskCreator CreatedBy { get; init; } = TaskCreator.User;

    public TaskKind Kind { get; init; } = TaskKind.Agent;

    /// <summary>Registered job of a system task.</summary>
    public string? Job { get; init; }

    public string? Name { get; init; }

    public TaskId? ParentId { get; init; }

    public string? Template { get; init; }

    public ProjectId? ProjectId { get; init; }

    public string? OriginSatelliteId { get; init; }

    public string? Notes { get; init; }

    public TaskBudget Budget { get; init; } = new();

    public IReadOnlyList<TaskDelivery> Deliveries { get; init; } = [];

    public DateTimeOffset? OverrideQuietHoursUntil { get; init; }
}
