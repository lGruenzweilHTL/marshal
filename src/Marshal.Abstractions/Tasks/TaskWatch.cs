using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Tasks;

/// <summary>
/// An armed <c>event</c> task: waiting for an event that matches <paramref name="Filter"/> (the trigger spec).
/// </summary>
public sealed record TaskWatch(TaskId Id, OwnerId OwnerId, string Filter);
