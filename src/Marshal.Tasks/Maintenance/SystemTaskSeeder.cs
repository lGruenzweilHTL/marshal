using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Tasks.Scheduling;
using Marshal.Tasks.SkillProposals;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Maintenance;

/// <summary>
/// Seeds the system tasks of the instance owner at startup, idempotent by name: <c>system-backup</c>,
/// <c>system-consolidation</c>, <c>system-retention</c> and <c>system-skill-proposals</c> (each only when its job is registered), cron tasks of kind
/// <c>system</c>. An existing one gets the configured schedule and is armed again unless it is running.
/// </summary>
public sealed partial class SystemTaskSeeder(
    IInstanceOwner instanceOwner,
    ITaskStore tasks,
    IUserProfileProvider profiles,
    IEnumerable<ISystemJob> jobs,
    IExecutionContextAccessor executionContext,
    IOptions<MaintenanceOptions> options,
    TimeProvider time,
    ILogger<SystemTaskSeeder> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        OwnerId owner = await instanceOwner.GetAsync(cancellationToken);
        HashSet<string> registered = [.. jobs.Select(job => job.Name)];
        using IDisposable scope = executionContext.Use(BackgroundTaskScheduler.SystemContext(owner));
        foreach (SystemTask system in Definitions(options.Value).Where(system => registered.Contains(system.Job)))
        {
            Result<TaskRecord> seeded = await SeedAsync(owner, system, cancellationToken);
            if (seeded.IsFailure)
                LogSeedFailed(system.Name, seeded.Error.Message);
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    internal static IReadOnlyList<SystemTask> Definitions(MaintenanceOptions options) =>
    [
        new("system-backup", BackupJob.JobName, options.BackupSchedule, "Nightly backup: database copy, Data Protection keys and the memory export of every owner."),
        new("system-consolidation", "consolidation", options.ConsolidationSchedule, "Nightly memory consolidation of the sessions closed during the day."),
        new("system-retention", RetentionPurgeJob.JobName, options.RetentionSchedule, "Retention purge: old run payloads and run metadata."),
        new("system-skill-proposals", SkillProposalJob.JobName, options.SkillProposalSchedule, "Skill proposals from workflows repeated in the last 30 days."),
    ];

    private async Task<Result<TaskRecord>> SeedAsync(OwnerId owner, SystemTask system, CancellationToken cancellationToken)
    {
        var trigger = TaskTrigger.Cron(system.Schedule);
        Result<TaskRecord> existing = await tasks.FindAsync(system.Name, cancellationToken);
        if (existing.IsFailure)
        {
            return await tasks.CreateAsync(new NewTask
            {
                Goal = system.Goal,
                Trigger = trigger,
                Kind = TaskKind.System,
                Job = system.Job,
                Name = system.Name,
                CreatedBy = TaskCreator.System,
            }, cancellationToken);
        }

        TaskRecord task = existing.Value;
        if (task.Status == AgentTaskStatus.Running)
            return task;

        TimeZoneInfo zone = await profiles.TimeZoneAsync(owner, cancellationToken);
        return await tasks.UpdateAsync(task with
        {
            Trigger = trigger,
            Status = task.Status == AgentTaskStatus.Waiting ? AgentTaskStatus.Waiting : AgentTaskStatus.Pending,
            StatusReason = null,
            NextRunAt = TaskTriggers.NextRun(trigger, time.GetUtcNow(), zone),
        }, cancellationToken);
    }

    internal sealed record SystemTask(string Name, string Job, string Schedule, string Goal);

    [LoggerMessage(Level = LogLevel.Error, Message = "Seeding system task {Name} failed: {Reason}")]
    private partial void LogSeedFailed(string name, string reason);
}
