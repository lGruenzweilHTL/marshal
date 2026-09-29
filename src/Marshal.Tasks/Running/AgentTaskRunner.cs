using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Core.Context;

namespace Marshal.Tasks.Running;

/// <summary>
/// Runs an agent task headless as its owner: the task's template and project, the origin satellite's policy (as the
/// run's satellite), background rules (not interactive, text output). The model gets the goal, the notes and the
/// trigger payload, never an earlier run's transcript. The reply's first paragraph is the result's speech, the whole
/// reply its body.
/// </summary>
public sealed class AgentTaskRunner(IAgentRunner agent, IUserProfileProvider profiles, ActiveTaskRuns activeRuns) : ITaskRunner
{
    /// <summary>Taint kind of runs started by an event payload (events can carry third-party text, e.g. mail subjects).</summary>
    public const string EventTaint = "event";

    private static readonly string[] _languages = ["de", "en"];

    public TaskKind Kind => TaskKind.Agent;

    public async Task<TaskRunOutcome> RunAsync(TaskRecord task, CancellationToken cancellationToken)
    {
        var context = new MarshalExecutionContext
        {
            OwnerId = task.OwnerId,
            Source = SessionSource.Task,
            InputMode = InputMode.Text,
            Language = await LanguageAsync(task.OwnerId, cancellationToken),
            SatelliteId = task.OriginSatelliteId,
            IsInteractive = false,
            Template = task.Template ?? "default",
            TaskId = task.Id,
            ProjectId = task.ProjectId,
        };
        if (task.Trigger.Kind == TaskTriggerKind.Event && task.TriggerPayload is not null)
            context.Taint.Mark(EventTaint, 0);

        using ActiveTaskRuns.Scope run = activeRuns.Begin(task.Id);
        AgentRunResult result = await agent.RunAsync(new AgentRunRequest { Context = context, Trigger = "task", UserText = Input(task) }, cancellationToken);

        if (result.Outcome != RunOutcome.Ok)
            return TaskRunOutcome.Failed(result.OutcomeReason ?? result.Outcome.ToString().ToLowerInvariant());
        if (run.State.WaitRequested)
            return TaskRunOutcome.Wait(run.State.WakeAt, run.State.Notes);
        return TaskRunOutcome.Done(ToResult(result.Reply), run.State.Notes);
    }

    private static string Input(TaskRecord task)
    {
        var input = new StringBuilder();
        input.Append("Task goal:\n").Append(task.Goal).Append("\n\nTask notes from earlier runs:\n")
            .Append(string.IsNullOrWhiteSpace(task.Notes) ? "(none)" : task.Notes)
            .Append("\n\nThis run was started by: ").Append(Describe(task.Trigger));
        if (task.TriggerPayload is not null)
        {
            input.Append("\n\nTrigger payload:\n").Append(task.Trigger.Kind == TaskTriggerKind.Event
                ? UntrustedContent.Wrap(EventTaint, task.TriggerPayload)
                : task.TriggerPayload);
        }

        return input.ToString();
    }

    private static string Describe(TaskTrigger trigger) => trigger.Kind switch
    {
        TaskTriggerKind.Cron => $"its schedule ({trigger.Spec})",
        TaskTriggerKind.At => "its scheduled time",
        TaskTriggerKind.Event => "a matching event",
        TaskTriggerKind.After => "the task it runs after",
        _ => "a request to run now",
    };

    private static TaskResult ToResult(string reply)
    {
        string body = reply.Trim();
        int split = body.IndexOf("\n\n", StringComparison.Ordinal);
        string speech = split < 0 ? body : body[..split].Trim();
        return new TaskResult(speech, body);
    }

    private async Task<string> LanguageAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        Result<UserProfile> profile = await profiles.GetAsync(owner, cancellationToken);
        return profile.IsSuccess && _languages.Contains(profile.Value.Language) ? profile.Value.Language : "en";
    }
}
