namespace Marshal.Tasks;

/// <summary>
/// The <c>tasks:</c> section of <c>marshal.yaml</c>.
/// </summary>
public sealed class TaskOptions
{
    /// <summary>Task runs that execute at the same time; further due tasks wait in order of their run time.</summary>
    public int Workers { get; set; } = 2;

    /// <summary>Budget limits of tasks that do not set their own.</summary>
    public TaskBudgetOptions DefaultBudget { get; set; } = new();

    /// <summary>Cost of all background runs of an owner per local day; no new run starts once it is reached.</summary>
    public decimal DailyCostCap { get; set; } = 2m;

    /// <summary>Deepest child task (a top-level task has depth 0).</summary>
    public int MaxDepth { get; set; } = 3;

    /// <summary>Unfinished child tasks a task may have at the same time (fan-out).</summary>
    public int MaxActiveChildren { get; set; } = 5;

    /// <summary>Shortest wakeup delay and cron period; templates may set their own.</summary>
    public TimeSpan MinWakeupInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a background approval waits for a decision; afterwards it expires and counts as rejected.</summary>
    public TimeSpan ApprovalTtl { get; set; } = TimeSpan.FromMinutes(10);
}

/// <summary>
/// Budget of one task activation (from its trigger through its wakeups until it is done).
/// </summary>
public sealed class TaskBudgetOptions
{
    /// <summary>Input + output tokens; null = unlimited.</summary>
    public long? Tokens { get; set; }

    public decimal Cost { get; set; } = 0.5m;

    /// <summary>Model calls.</summary>
    public int Steps { get; set; } = 60;

    public TimeSpan WallTime { get; set; } = TimeSpan.FromMinutes(30);

    public int Runs { get; set; } = 20;
}
