using Marshal.Abstractions;

namespace Marshal.Tasks.Approvals;

/// <summary>Expected failures of approval decisions.</summary>
public static class ApprovalErrors
{
    public static Error NotFound() => new("approval.not_found", "There is no such approval.", ErrorType.NotFound);

    public static Error AlreadyDecided() => new("approval.decided", "The approval was already decided.", ErrorType.Conflict);

    public static Error Expired() => new("approval.expired", "The approval expired; the action was not executed.", ErrorType.Conflict);

    public static Error TaskEnded() => new("approval.task_ended", "The task no longer waits for this approval; the action was not executed.", ErrorType.Conflict);

    public static Error TaskBusy() => new("approval.task_busy", "The task is still finishing its run; try again in a moment.", ErrorType.Conflict);
}
