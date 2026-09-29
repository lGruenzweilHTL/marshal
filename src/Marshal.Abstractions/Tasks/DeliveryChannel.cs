namespace Marshal.Abstractions.Tasks;

/// <summary>
/// Where a task delivery goes.
/// </summary>
public enum DeliveryChannel
{
    /// <summary>Spoken on a satellite (a named one, or where the owner is).</summary>
    Voice,

    /// <summary>A companion app push notification.</summary>
    Push,

    /// <summary>The owner's inbox.</summary>
    Inbox,

    /// <summary>No notification; the result is only visible in the web UI.</summary>
    Silent,
}
