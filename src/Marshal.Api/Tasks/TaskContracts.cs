using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;

namespace Marshal.Api.Tasks;

/// <summary>A task or watch in the tasks list.</summary>
/// <param name="Id">Task id.</param>
/// <param name="Name">Unique name, if any.</param>
/// <param name="Goal">What the task works on.</param>
/// <param name="Kind"><c>agent</c> or <c>system</c>.</param>
/// <param name="Status">Lifecycle state.</param>
/// <param name="StatusReason">Why it is in that state (e.g. <c>budget_exceeded</c>, <c>paused</c>).</param>
/// <param name="Trigger">What makes it due; watches have kind <c>event</c>.</param>
/// <param name="NextRunAt">When it runs next.</param>
/// <param name="Budget">Limits over one activation.</param>
/// <param name="Spent">What the current activation has used.</param>
/// <param name="ResultSpeech">The spoken part of the latest result.</param>
/// <param name="ResultAt">When the latest result was ready.</param>
/// <param name="ParentId">The task that created it.</param>
/// <param name="CreatedBy">Who created it.</param>
/// <param name="CreatedAt">When it was created.</param>
/// <param name="UpdatedAt">When it last changed.</param>
public sealed record TaskItem(
    string Id,
    string? Name,
    string Goal,
    TaskKind Kind,
    AgentTaskStatus Status,
    string? StatusReason,
    TaskTrigger Trigger,
    DateTimeOffset? NextRunAt,
    TaskBudgetView Budget,
    TaskSpendView Spent,
    string? ResultSpeech,
    DateTimeOffset? ResultAt,
    string? ParentId,
    TaskCreator CreatedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    internal static TaskItem From(TaskRecord task) => new(
        task.Id.ToString(),
        task.Name,
        task.Goal,
        task.Kind,
        task.Status,
        task.StatusReason,
        task.Trigger,
        task.NextRunAt,
        new TaskBudgetView(task.Budget.Tokens, task.Budget.Cost, task.Budget.Steps, task.Budget.WallTime?.TotalSeconds, task.Budget.Runs),
        new TaskSpendView(task.Spent.Tokens, task.Spent.Cost, task.Spent.Steps, task.Spent.WallTime.TotalSeconds, task.Spent.Runs),
        task.Result?.Speech,
        task.ResultAt,
        task.ParentId?.ToString(),
        task.CreatedBy,
        task.CreatedAt,
        task.UpdatedAt);
}

/// <summary>Limits of a task over one activation; null limits are not enforced at the task level.</summary>
/// <param name="Tokens">Model tokens.</param>
/// <param name="Cost">Cost in the configured currency.</param>
/// <param name="Steps">Model calls.</param>
/// <param name="WallTimeSeconds">Run time.</param>
/// <param name="Runs">Runs.</param>
public sealed record TaskBudgetView(long? Tokens, decimal? Cost, int? Steps, double? WallTimeSeconds, int? Runs);

/// <summary>What a task's current activation has used, in the shape of <see cref="TaskBudgetView"/>.</summary>
/// <param name="Tokens">Model tokens.</param>
/// <param name="Cost">Cost in the configured currency.</param>
/// <param name="Steps">Model calls.</param>
/// <param name="WallTimeSeconds">Run time.</param>
/// <param name="Runs">Runs.</param>
public sealed record TaskSpendView(long Tokens, decimal Cost, int Steps, double WallTimeSeconds, int Runs);

/// <summary>One task with everything the detail view shows.</summary>
/// <param name="Task">The task as listed.</param>
/// <param name="Notes">Notes the task keeps between runs.</param>
/// <param name="Result">The latest result: speech and markdown body.</param>
/// <param name="Deliveries">Where results go.</param>
/// <param name="OverrideQuietHoursUntil">Until when it may notify during quiet hours.</param>
/// <param name="Runs">Its runs, newest first.</param>
/// <param name="Notifications">What it sent and what the router did, newest first.</param>
/// <param name="ExecutorSessions">External work it started, newest first.</param>
public sealed record TaskDetail(
    TaskItem Task,
    string? Notes,
    TaskResult? Result,
    IReadOnlyList<TaskDelivery> Deliveries,
    DateTimeOffset? OverrideQuietHoursUntil,
    IReadOnlyList<RunSummary> Runs,
    IReadOnlyList<NotificationEntry> Notifications,
    IReadOnlyList<ExecutorSessionRecord> ExecutorSessions);

/// <summary>A new goal for a task.</summary>
/// <param name="Goal">The goal the next run works on.</param>
public sealed record TaskGoalUpdate(string Goal);

/// <summary>The outcome of a cancellation.</summary>
/// <param name="Cancelled">How many tasks were cancelled (the task and its unfinished descendants).</param>
public sealed record TaskCancellation(int Cancelled);

/// <summary>The kill switch.</summary>
/// <param name="Active">Whether background work is stopped.</param>
/// <param name="Cancelled">On activation: how many tasks were cancelled.</param>
public sealed record KillSwitchState(bool Active, int? Cancelled = null);
