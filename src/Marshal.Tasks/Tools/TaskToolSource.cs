using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Tools;
using Marshal.Tasks.Control;
using Marshal.Tasks.Limits;
using Marshal.Tasks.Running;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Tools;

/// <summary>
/// The built-in <c>tasks.*</c> tools.
/// </summary>
public sealed class TaskToolSource(
    ITaskStore store,
    ITaskScheduler scheduler,
    IKillSwitch killSwitch,
    TaskBudgets budgets,
    ActiveTaskRuns runs,
    IUserProfileProvider profiles,
    IOptions<TaskOptions> options,
    TimeProvider time) : IToolSource
{
    private readonly IReadOnlyList<ITool> _tools = TaskTools.Create(new TaskToolContext(store, scheduler, new TaskControl(store, scheduler), killSwitch, budgets, runs, profiles, options, time));

    public string Name => "tasks";

    public bool IsBuiltIn => true;

    public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) => Task.FromResult(_tools);
}

/// <summary>
/// What the task tools share.
/// </summary>
internal sealed record TaskToolContext(
    ITaskStore Store,
    ITaskScheduler Scheduler,
    ITaskControl Control,
    IKillSwitch KillSwitch,
    TaskBudgets Budgets,
    ActiveTaskRuns Runs,
    IUserProfileProvider Profiles,
    IOptions<TaskOptions> Options,
    TimeProvider Time);
