using Marshal.Abstractions.Identity;

namespace Marshal.Abstractions.Tasks;

/// <summary>
/// A task id with the owner to act as, returned by the instance-wide scheduler queries.
/// </summary>
public sealed record OwnedTask(TaskId Id, OwnerId OwnerId);
