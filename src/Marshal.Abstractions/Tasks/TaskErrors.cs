namespace Marshal.Abstractions.Tasks;

/// <summary>Expected failures of the task system.</summary>
public static class TaskErrors
{
    public static Error Invalid(string message) => new("tasks.invalid", message, ErrorType.Validation);

    public static Error NameTaken(string name) => new("tasks.name_taken", $"A task named '{name}' already exists.", ErrorType.Validation);

    public static Error NotFound(string reference) => new("tasks.not_found", $"There is no task '{reference}'.", ErrorType.NotFound);

    public static Error BudgetExceeded() => new("tasks.budget_exceeded", "The task's budget (or its parent's) is used up.", ErrorType.Conflict);

    public static Error DailyCap() => new("tasks.daily_cap", "Today's cost cap for background tasks is reached.", ErrorType.Conflict);

    public static Error Paused() => new("tasks.paused", "Background tasks are stopped (kill switch).", ErrorType.Conflict);

    public static Error Conflict() => new("tasks.conflict", "The task was changed by someone else; reload it and try again.", ErrorType.Conflict);
}
