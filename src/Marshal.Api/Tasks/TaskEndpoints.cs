using System.Security.Claims;
using Marshal.Abstractions;
using Marshal.Abstractions.Audit;
using Marshal.Abstractions.Executors;
using Marshal.Abstractions.Notifications;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Api.Auth;
using Marshal.Api.Problems;
using Marshal.Tasks.Policies;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Marshal.Api.Tasks;

/// <summary>
/// The tasks view: the user's tasks and watches, their detail, goal and tool policy edits and cancellation (audited), and the kill
/// switch (audited by the switch itself).
/// </summary>
internal static class TaskEndpoints
{
    private const int DetailRuns = 20;
    private const int DetailNotifications = 20;

    private static readonly AgentTaskStatus[] _active = [AgentTaskStatus.Pending, AgentTaskStatus.Running, AgentTaskStatus.Waiting];

    public static RouteGroupBuilder MapTaskEndpoints(this RouteGroupBuilder group)
    {
        RouteGroupBuilder tasks = group.MapGroup("/tasks").WithTags("Tasks").RequireAuthorization(Policies.Human);
        tasks.MapGet("/", ListAsync).WithName("ListTasks")
            .WithDescription("The user's tasks and watches, oldest first; status filters by one status or active (pending, running, waiting).");
        tasks.MapGet("/kill-switch", async (IKillSwitch killSwitch, CancellationToken ct) => TypedResults.Ok(new KillSwitchState(await killSwitch.IsActiveAsync(ct))))
            .WithName("GetKillSwitch").WithDescription("Whether background work is stopped.");
        tasks.MapPost("/kill-switch/activate", async (IKillSwitch killSwitch, CancellationToken ct) => TypedResults.Ok(new KillSwitchState(true, await killSwitch.ActivateAsync(ct))))
            .WithName("ActivateKillSwitch").WithDescription("Stops every unfinished agent task and pauses new ones until released.");
        tasks.MapPost("/kill-switch/release", async (IKillSwitch killSwitch, CancellationToken ct) =>
            {
                await killSwitch.ReleaseAsync(ct);
                return TypedResults.Ok(new KillSwitchState(false));
            })
            .WithName("ReleaseKillSwitch").WithDescription("Lets background tasks run again; tasks paused while it was active become due.");
        tasks.MapGet("/{id}", GetAsync).WithName("GetTask")
            .WithDescription("One task with notes, the latest result, deliveries, recent runs, notifications and executor sessions.");
        tasks.MapPut("/{id}/goal", SetGoalAsync).WithName("SetTaskGoal")
            .WithDescription("Replaces the task's goal; the next run uses it.");
        tasks.MapPut("/{id}/tool-policy", SetToolPolicyAsync).WithName("SetTaskToolPolicy")
            .WithDescription("Replaces the task's tool policy (tool ids or globs inside its template's allowlist); the next run sees only those tools.");
        tasks.MapPost("/{id}/cancel", CancelAsync).WithName("CancelTask")
            .WithDescription("Cancels the task and its unfinished child tasks.");
        return group;
    }

    private static async Task<Results<Ok<List<TaskItem>>, ProblemHttpResult>> ListAsync(string? status, ITaskStore store, CancellationToken ct)
    {
        IReadOnlyCollection<AgentTaskStatus>? statuses;
        if (status is null)
            statuses = null;
        else if (status == "active")
            statuses = _active;
        else if (Enum.TryParse(status, ignoreCase: true, out AgentTaskStatus parsed) && !int.TryParse(status, out _))
            statuses = [parsed];
        else
            return new Error("tasks.invalid_filter", "Unknown status.", ErrorType.Validation).ToProblem();

        return TypedResults.Ok((await store.ListAsync(new TaskFilter(statuses), ct)).Select(TaskItem.From).ToList());
    }

    private static async Task<Results<Ok<TaskDetail>, ProblemHttpResult>> GetAsync(
        TaskId id, ITaskStore store, IRunQueries runs, INotificationHistory notifications, IExecutorSessions sessions, IToolRegistry registry, ITemplateCatalog templates,
        CancellationToken ct)
    {
        Result<TaskRecord> found = await store.GetAsync(id, ct);
        if (found.IsFailure)
            return found.Error.ToProblem();

        TaskRecord task = found.Value;
        return TypedResults.Ok(new TaskDetail(
            TaskItem.From(task),
            task.Notes,
            task.Result,
            task.Deliveries,
            task.OverrideQuietHoursUntil,
            await runs.ListAsync(new RunFilter(Limit: DetailRuns, TaskId: id), ct),
            await notifications.ListAsync(new NotificationQuery(TaskId: id, Limit: DetailNotifications), ct),
            await sessions.ListAsync(id, ct),
            task.ToolPolicy,
            AvailableTools(task, registry, templates),
            TaskPreparationInfo.From(task)));
    }

    private static async Task<Results<Ok<TaskItem>, ProblemHttpResult>> SetToolPolicyAsync(
        TaskId id, TaskToolPolicyUpdate update, ClaimsPrincipal user, ITaskStore store, ITaskToolPolicy policy, IAuditLog audit, CancellationToken ct)
    {
        Result<TaskRecord> found = await store.GetAsync(id, ct);
        if (found.IsFailure)
            return found.Error.ToProblem();
        if (update.Tools is not { Count: > 0 } tools)
            return new Error(DefaultTaskToolPolicy.InvalidToolsCode, "tools must list at least one tool id or glob.", ErrorType.Validation).ToProblem();

        Result valid = policy.Check(found.Value.Template, tools);
        if (valid.IsFailure)
            return valid.Error.ToProblem();

        Result<TaskRecord> saved = await store.SetToolPolicyAsync(id, [.. tools.Select(tool => tool.Trim()).Distinct(StringComparer.Ordinal)], ct);
        if (saved.IsFailure)
            return saved.Error.ToProblem();

        await AuditAsync(user, audit, "task.update_tool_policy", id, new { previous = found.Value.ToolPolicy, tools = saved.Value.ToolPolicy }, ct);
        return TypedResults.Ok(TaskItem.From(saved.Value));
    }

    private static List<string> AvailableTools(TaskRecord task, IToolRegistry registry, ITemplateCatalog templates)
    {
        AgentTemplate template = templates.Find(task.Template ?? AgentTemplate.DefaultName) ?? templates.Find(AgentTemplate.DefaultName) ?? AgentTemplate.BuiltInDefault;
        return [.. registry.Current.Tools.Where(tool => template.Allows(tool.Id)).Select(tool => tool.Id.ToString()).Order(StringComparer.Ordinal)];
    }

    private static async Task<Results<Ok<TaskItem>, ProblemHttpResult>> SetGoalAsync(
        TaskId id, TaskGoalUpdate update, ClaimsPrincipal user, ITaskControl control, IAuditLog audit, CancellationToken ct)
    {
        Result<TaskRecord> saved = await control.SetGoalAsync(id, update.Goal ?? "", ct);
        if (saved.IsFailure)
            return saved.Error.ToProblem();

        await AuditAsync(user, audit, "task.update_goal", id, new { goal = saved.Value.Goal }, ct);
        return TypedResults.Ok(TaskItem.From(saved.Value));
    }

    private static async Task<Results<Ok<TaskCancellation>, ProblemHttpResult>> CancelAsync(
        TaskId id, ClaimsPrincipal user, ITaskControl control, IAuditLog audit, CancellationToken ct)
    {
        Result<int> cancelled = await control.CancelAsync(id, ct);
        if (cancelled.IsFailure)
            return cancelled.Error.ToProblem();

        await AuditAsync(user, audit, "task.cancel", id, new { cancelled = cancelled.Value }, ct);
        return TypedResults.Ok(new TaskCancellation(cancelled.Value));
    }

    private static Task AuditAsync(ClaimsPrincipal user, IAuditLog audit, string action, TaskId id, object details, CancellationToken ct)
    {
        (AuditActorKind kind, string actor) = user.Actor();
        return audit.RecordAsync(new AuditEntry(kind, actor, action, id.ToString(), details), ct);
    }
}
