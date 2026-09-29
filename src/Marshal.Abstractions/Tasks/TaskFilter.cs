namespace Marshal.Abstractions.Tasks;

/// <summary>
/// Which of the owner's tasks to list. Null criteria match every task.
/// </summary>
public sealed record TaskFilter(IReadOnlyCollection<AgentTaskStatus>? Statuses = null, TaskId? ParentId = null);
