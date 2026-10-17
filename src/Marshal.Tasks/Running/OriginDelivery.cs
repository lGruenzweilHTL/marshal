using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tasks;

namespace Marshal.Tasks.Running;

/// <summary>
/// Where a task created on behalf of a turn delivers: the satellite of a voice turn (push without one), the inbox
/// otherwise.
/// </summary>
internal static class OriginDelivery
{
    public static TaskDelivery For(MarshalExecutionContext context) => context switch
    {
        { InputMode: InputMode.Voice, SatelliteId: { } satellite } => new TaskDelivery(DeliveryChannel.Voice, satellite),
        { InputMode: InputMode.Voice } => new TaskDelivery(DeliveryChannel.Push),
        _ => new TaskDelivery(DeliveryChannel.Inbox),
    };
}
