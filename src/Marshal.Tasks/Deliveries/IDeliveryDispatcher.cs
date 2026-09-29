using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Deliveries;

/// <summary>
/// Delivers task results: each delivery on its own, at once or when its conditions hold, or on its fallback channel
/// when it expires first.
/// </summary>
internal interface IDeliveryDispatcher
{
    /// <summary>Delivers the current result of <paramref name="task"/> (a task of the current owner).</summary>
    Task DispatchAsync(TaskId task, CancellationToken cancellationToken);

    /// <summary>
    /// Re-checks waiting deliveries of every owner: delivers the ones whose conditions now hold (only those that
    /// depend on <paramref name="changedEntity"/>, when given) and the expired ones on their fallback channel.
    /// </summary>
    Task ReviewPendingAsync(string? changedEntity, CancellationToken cancellationToken);
}
