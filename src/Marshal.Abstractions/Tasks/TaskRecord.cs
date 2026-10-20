using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Sessions;

namespace Marshal.Abstractions.Tasks;

/// <summary>
/// A stored task. Change it with <c>with</c> and <see cref="ITaskStore.UpdateAsync"/>; the update fails when
/// <see cref="RowVersion"/> is no longer current.
/// </summary>
public sealed record TaskRecord
{
    public required TaskId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public TaskId? ParentId { get; init; }

    public required TaskCreator CreatedBy { get; init; }

    public required TaskKind Kind { get; init; }

    public required string Goal { get; init; }

    public string? Job { get; init; }

    public string? Template { get; init; }

    public ProjectId? ProjectId { get; init; }

    public string? OriginSatelliteId { get; init; }

    /// <summary>Conversation a background handoff came from; the result is shown there.</summary>
    public SessionId? OriginSessionId { get; init; }

    public required TaskTrigger Trigger { get; init; }

    /// <summary>
    /// Tool id globs the task's runs are limited to, inside the template's allowlist; null (tasks created before tool
    /// policies, or without a session) means the template's allowlist. Changed only through
    /// <see cref="ITaskStore.SetToolPolicyAsync"/>.
    /// </summary>
    public IReadOnlyList<string>? ToolPolicy { get; init; }

    /// <summary>JSON payload of the trigger that made the task due.</summary>
    public string? TriggerPayload { get; init; }

    public required AgentTaskStatus Status { get; init; }

    public string? StatusReason { get; init; }

    public DateTimeOffset? NextRunAt { get; init; }

    public string? Notes { get; init; }

    public required TaskBudget Budget { get; init; }

    public required TaskSpend Spent { get; init; }

    public string? Name { get; init; }

    public required IReadOnlyList<TaskDelivery> Deliveries { get; init; }

    public DateTimeOffset? OverrideQuietHoursUntil { get; init; }

    public TaskResult? Result { get; init; }

    public DateTimeOffset? ResultAt { get; init; }

    public required int Depth { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }

    public required long RowVersion { get; init; }
}
