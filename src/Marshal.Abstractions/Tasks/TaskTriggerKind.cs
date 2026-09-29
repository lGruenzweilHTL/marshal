namespace Marshal.Abstractions.Tasks;

/// <summary>
/// What makes a task due.
/// </summary>
public enum TaskTriggerKind
{
    /// <summary>Due immediately.</summary>
    Now,

    /// <summary>Due at a point in time (owner's time zone, stored in UTC).</summary>
    At,

    /// <summary>Due on a cron schedule in the owner's time zone.</summary>
    Cron,

    /// <summary>Due when a matching event arrives.</summary>
    Event,

    /// <summary>Due when another task finishes.</summary>
    After,
}
