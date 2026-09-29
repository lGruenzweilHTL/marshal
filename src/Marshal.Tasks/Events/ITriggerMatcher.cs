using Marshal.Abstractions.Events;

namespace Marshal.Tasks.Events;

/// <summary>
/// Makes the tasks whose event filter matches an event due, without a model call.
/// </summary>
internal interface ITriggerMatcher
{
    Task MatchAsync(MarshalEvent @event, CancellationToken cancellationToken);
}
