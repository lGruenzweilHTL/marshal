namespace Marshal.Abstractions.Tasks;

/// <summary>
/// What a task has used so far, in the shape of <see cref="TaskBudget"/>. Child tasks also count against their parent.
/// </summary>
public sealed record TaskSpend(long Tokens = 0, decimal Cost = 0, int Steps = 0, TimeSpan WallTime = default, int Runs = 0)
{
    public static TaskSpend Zero { get; } = new();

    public TaskSpend Add(TaskSpend other) =>
        new(Tokens + other.Tokens, Cost + other.Cost, Steps + other.Steps, WallTime + other.WallTime, Runs + other.Runs);
}
