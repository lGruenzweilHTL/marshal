using Marshal.Abstractions;

namespace Marshal.Tasks.InputRequests;

/// <summary>Expected failures of answering input requests.</summary>
public static class InputRequestErrors
{
    public static Error NotFound() => new("input_request.not_found", "There is no such input request.", ErrorType.NotFound);

    public static Error AlreadyDecided() => new("input_request.decided", "The input request was already answered or declined.", ErrorType.Conflict);

    public static Error Expired() => new("input_request.expired", "The input request expired; nothing was sent to the server.", ErrorType.Conflict);

    public static Error TaskBusy() => new("input_request.task_busy", "The task is still finishing its run; try again in a moment.", ErrorType.Conflict);

    public static Error InvalidAnswer(string detail) => new("input_request.invalid_answer", detail, ErrorType.Validation);
}
