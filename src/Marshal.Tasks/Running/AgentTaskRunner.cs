using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Guard;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Core.Context;
using Marshal.Tasks.Approvals;
using Marshal.Tasks.FollowUps;

namespace Marshal.Tasks.Running;

/// <summary>
/// Runs an agent task headless as its owner: the task's template and project, the origin satellite's policy (as the
/// run's satellite), background rules (not interactive, text output). The model gets the goal, the notes and the
/// trigger payload, never an earlier run's transcript; the one exception is the first run of a turn moved to the
/// background, which continues that turn's transcript (and its taint). The reply's first paragraph is the result's
/// speech, the whole reply its body.
/// </summary>
public sealed class AgentTaskRunner(IAgentRunner agent, IUserProfileProvider profiles, ActiveTaskRuns activeRuns, TaskContinuations continuations) : ITaskRunner
{
    /// <summary>Taint kind of runs started by an event payload (events can carry third-party text, e.g. mail subjects).</summary>
    public const string EventTaint = "event";

    private static readonly string[] _languages = ["de", "en"];

    public TaskKind Kind => TaskKind.Agent;

    public async Task<TaskRunOutcome> RunAsync(TaskRecord task, RunLimits limits, CancellationToken cancellationToken)
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
        var request = new AgentRunRequest { Context = context, Trigger = "task", UserText = Input(task), Limits = limits };
        if (continuations.TryTake(task.Id, out Continuation? continuation))
        {
            request = request with { Trigger = "continuation", History = continuation.Transcript, UserText = null };
            foreach (TaintSource source in continuation.Taint)
                context.Taint.Mark(source.Kind, 0);
        }
        else if (PayloadTaint(task) is { } taint)
        {
            context.Taint.Mark(taint, 0);
        }

        using ActiveTaskRuns.Scope run = activeRuns.Begin(task.Id);
        AgentRunResult result = await agent.RunAsync(request, cancellationToken);

        TaskRunOutcome outcome;
        if (result.Outcome != RunOutcome.Ok)
            outcome = TaskRunOutcome.Failed(result.OutcomeReason ?? result.Outcome.ToString().ToLowerInvariant());
        else if (result.PendingAction is { } approval)
            outcome = TaskRunOutcome.Wait(null, ApprovalNote(run.State.Notes ?? task.Notes, approval));
        else if (run.State.WaitRequested)
            outcome = TaskRunOutcome.Wait(run.State.WakeAt, run.State.Notes);
        else
            outcome = TaskRunOutcome.Done(ToResult(result.Reply), run.State.Notes);
        return outcome with { Spend = new TaskSpend(context.Usage.Tokens, context.Usage.Cost, context.Usage.ModelCalls) };
    }

    // A server-written note, so the resumed run knows what it waited for.
    private static string ApprovalNote(string? notes, PendingActionRecord approval)
    {
        string note = $"approval requested: {approval.Tool}: {approval.Summary} ({approval.Id})";
        return string.IsNullOrWhiteSpace(notes) ? note : $"{notes}\n{note}";
    }

    private static string Input(TaskRecord task)
    {
        var input = new StringBuilder();
        input.Append("Task goal:\n").Append(task.Goal).Append("\n\nTask notes from earlier runs:\n")
            .Append(string.IsNullOrWhiteSpace(task.Notes) ? "(none)" : task.Notes)
            .Append("\n\nThis run was started by: ").Append(Describe(task));
        if (task.TriggerPayload is not null)
        {
            input.Append("\n\nTrigger payload:\n").Append(PayloadTaint(task) is { } taint
                ? UntrustedContent.Wrap(taint, task.TriggerPayload)
                : task.TriggerPayload);
        }

        return input.ToString();
    }

    // Approval decisions are written by the server; they are untrusted only when the approved action returned
    // untrusted content. A spoken reply is the user's own. Other event payloads can carry third-party text.
    private static string? PayloadTaint(TaskRecord task)
    {
        if (task.TriggerPayload is null)
            return null;
        if (Payload(task, ApprovalQueue.PayloadKey) is { } approval)
            return approval.TryGetProperty(ApprovalQueue.TaintKey, out JsonElement taint) && taint.ValueKind == JsonValueKind.String ? taint.GetString() : null;
        if (Payload(task, FollowUpReplies.PayloadKey) is not null)
            return null;
        return task.Trigger.Kind == TaskTriggerKind.Event ? EventTaint : null;
    }

    // The payload object when it has the top-level key (approval decision, spoken reply), else null.
    private static JsonElement? Payload(TaskRecord task, string key)
    {
        if (task.TriggerPayload is null)
            return null;
        try
        {
            using var document = JsonDocument.Parse(task.TriggerPayload);
            return document.RootElement.ValueKind == JsonValueKind.Object && document.RootElement.TryGetProperty(key, out _)
                ? document.RootElement.Clone()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Describe(TaskRecord task) =>
        Payload(task, ApprovalQueue.PayloadKey) is not null ? "an approval decision"
        : Payload(task, FollowUpReplies.PayloadKey) is not null ? "the user's spoken reply to your question"
        : task.Trigger.Kind switch
    {
        TaskTriggerKind.Cron => $"its schedule ({task.Trigger.Spec})",
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
