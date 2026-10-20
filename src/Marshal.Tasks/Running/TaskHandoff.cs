using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Tasks.KillSwitch;
using Marshal.Tasks.Policies;

namespace Marshal.Tasks.Running;

/// <summary>
/// <see cref="IBackgroundHandoff"/>: a task for the turn's user, due now, with the turn's satellite as origin (its
/// policy keeps applying), the result delivered by voice to that satellite (voice turns; push without a satellite)
/// or to the inbox (text turns), the turn's session as origin (its result joins that conversation), and the transcript
/// for its first run. Its tool policy is derived from the turn like a task the turn creates. Refused while the kill
/// switch is active.
/// </summary>
public sealed class TaskHandoff(ITaskStore tasks, TaskPauses pauses, TaskContinuations continuations, ITaskScheduler scheduler, ITaskToolPolicy? policy = null) : IBackgroundHandoff
{
    public async Task<Result<TaskId>> HandOffAsync(BackgroundHandoff handoff, CancellationToken cancellationToken)
    {
        MarshalExecutionContext context = handoff.Context;
        if (await pauses.IsPausedAsync(context.OwnerId, cancellationToken))
            return TaskErrors.Paused();

        Result<TaskRecord> created = await tasks.CreateAsync(new NewTask
        {
            Goal = handoff.Request,
            Trigger = TaskTrigger.Now(),
            CreatedBy = TaskCreator.User,
            Template = context.Template,
            ProjectId = context.ProjectId,
            OriginSatelliteId = context.SatelliteId,
            OriginSessionId = context.SessionId,
            Notes = $"Continues a turn moved to the background ({handoff.Reason}, run {context.RunId}).",
            Deliveries = [Delivery(context)],
            ToolPolicy = policy?.Derive(context.Template, context, null) is { IsSuccess: true } derived ? derived.Value : null,
        }, cancellationToken);
        if (created.IsFailure)
            return created.Error;

        continuations.Add(created.Value.Id, new Continuation(handoff.Transcript, context.Taint.Sources));
        scheduler.Wake();
        return created.Value.Id;
    }

    private static TaskDelivery Delivery(MarshalExecutionContext context) => context switch
    {
        { InputMode: InputMode.Voice, SatelliteId: { } satellite } => new TaskDelivery(DeliveryChannel.Voice, satellite),
        { InputMode: InputMode.Voice } => new TaskDelivery(DeliveryChannel.Push),
        _ => new TaskDelivery(DeliveryChannel.Inbox),
    };
}
