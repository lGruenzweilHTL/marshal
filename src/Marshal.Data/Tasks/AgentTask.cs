using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Tasks;

namespace Marshal.Data.Tasks;

/// <summary>
/// A durable background task. Runs are recorded as <see cref="Conversations.Run"/> rows with this task's id.
/// </summary>
public sealed class AgentTask : IOwned, IVersioned
{
    public required TaskId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public TaskId? ParentId { get; init; }

    public required TaskCreator CreatedBy { get; init; }

    public required TaskKind Kind { get; init; }

    /// <summary>What the agent should achieve; for system tasks a description.</summary>
    public required string Goal { get; set; }

    /// <summary>Registered job name of a system task.</summary>
    public string? Job { get; init; }

    public string? Template { get; set; }

    public ProjectId? ProjectId { get; init; }

    /// <summary>Satellite the task was created from; its policy applies to all runs and child tasks.</summary>
    public string? OriginSatelliteId { get; init; }

    public required TaskTriggerKind TriggerKind { get; set; }

    /// <summary>Time (ISO 8601), cron expression, event filter JSON or task id, depending on the kind.</summary>
    public required string TriggerSpec { get; set; }

    /// <summary>An <c>event</c> trigger that fires once and then ends instead of re-arming.</summary>
    public bool TriggerOnce { get; set; }

    /// <summary>JSON payload of the trigger that made the task due, handed to the next run.</summary>
    public string? TriggerPayload { get; set; }

    public AgentTaskStatus Status { get; set; } = AgentTaskStatus.Pending;

    /// <summary>Why the task is in its status (e.g. <c>budget_exceeded</c>).</summary>
    public string? StatusReason { get; set; }

    public DateTimeOffset? NextRunAt { get; set; }

    /// <summary>The task's notes, kept between runs (written by the model and the server).</summary>
    public string? Notes { get; set; }

    /// <summary>JSON budget: tokens, cost, steps, wall time, runs.</summary>
    public string Budget { get; set; } = "{}";

    /// <summary>JSON spend in the same shape as <see cref="Budget"/>.</summary>
    public string Spent { get; set; } = "{}";

    /// <summary>Optional name, unique per owner.</summary>
    public string? Name { get; set; }

    /// <summary>JSON array of deliveries, with the state of pending conditional ones.</summary>
    public string Deliveries { get; set; } = "[]";

    /// <summary>Whether a delivery waits for its conditions (kept in step with <see cref="Deliveries"/>).</summary>
    public bool DeliveriesPending { get; set; }

    public DateTimeOffset? OverrideQuietHoursUntil { get; set; }

    public string? ResultSpeech { get; set; }

    /// <summary>Markdown result body.</summary>
    public string? ResultBody { get; set; }

    public DateTimeOffset? ResultAt { get; set; }

    public int Depth { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; set; }

    /// <inheritdoc />
    public long RowVersion { get; set; }
}
