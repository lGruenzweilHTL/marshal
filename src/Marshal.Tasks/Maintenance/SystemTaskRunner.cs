using Marshal.Abstractions;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Tasks.Running;
using Microsoft.Extensions.Logging;

namespace Marshal.Tasks.Maintenance;

/// <summary>
/// Runs <c>system</c> tasks: the <see cref="ISystemJob"/> named by the task's job, no agent and no tokens. A failed
/// or crashed job still ends the run <c>done</c> with the failure as its result, so a nightly schedule keeps running.
/// </summary>
public sealed partial class SystemTaskRunner(IEnumerable<ISystemJob> jobs, ILogger<SystemTaskRunner> logger) : ITaskRunner
{
    private readonly Dictionary<string, ISystemJob> _jobs = jobs.ToDictionary(job => job.Name, StringComparer.Ordinal);

    public TaskKind Kind => TaskKind.System;

    public async Task<TaskRunOutcome> RunAsync(TaskRecord task, RunLimits limits, CancellationToken cancellationToken)
    {
        if (task.Job is not { } name || !_jobs.TryGetValue(name, out ISystemJob? job))
            return TaskRunOutcome.Failed("unknown_job");

        string text;
        try
        {
            Result<string> result = await job.RunAsync(cancellationToken);
            text = result.IsSuccess ? result.Value : $"{name} failed: {result.Error.Message}";
            if (result.IsFailure)
                LogFailed(name, result.Error.Message);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            LogCrashed(name, exception);
            text = $"{name} failed: {exception.Message}";
        }

        return TaskRunOutcome.Done(new TaskResult(text, text));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "System job {Job} failed: {Reason}")]
    private partial void LogFailed(string job, string reason);

    [LoggerMessage(Level = LogLevel.Error, Message = "System job {Job} crashed")]
    private partial void LogCrashed(string job, Exception exception);
}
