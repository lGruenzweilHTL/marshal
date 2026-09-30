using System.Globalization;
using Marshal.Abstractions;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Data.Conversations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Maintenance;

/// <summary>
/// Retention purge (system job <c>retention</c>), across owners: run step payloads (prompts, tool arguments and
/// results, replies) of runs older than <see cref="MaintenanceOptions.PayloadRetention"/> are deleted and their
/// metadata stays; runs older than <see cref="MaintenanceOptions.MetadataRetention"/> are deleted with their steps.
/// Audit events and project feedback that point to a deleted run keep their row without the link. Memory is not
/// touched: chunks are not logs.
/// </summary>
public sealed class RetentionPurgeJob(IDbContextFactory<MarshalDbContext> contexts, IOptions<MaintenanceOptions> options, TimeProvider time) : ISystemJob
{
    public const string JobName = "retention";

    public string Name => JobName;

    public async Task<Result<string>> RunAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = time.GetUtcNow();
        DateTimeOffset payloadCutoff = now - options.Value.PayloadRetention;
        DateTimeOffset metadataCutoff = now - options.Value.MetadataRetention;

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        IQueryable<Run> runs = db.Runs.IgnoreQueryFilters();
        int payloads = await db.RunSteps.IgnoreQueryFilters()
            .Where(step => step.Payload != null && runs.Any(run => run.Id == step.RunId && run.StartedAt < payloadCutoff))
            .ExecuteUpdateAsync(update => update.SetProperty(step => step.Payload, (string?)null), cancellationToken);

        IQueryable<RunId> expired = runs.Where(run => run.StartedAt < metadataCutoff).Select(run => run.Id);
        await db.AuditEvents.IgnoreQueryFilters()
            .Where(audit => audit.RunId != null && expired.Contains(audit.RunId.Value))
            .ExecuteUpdateAsync(update => update.SetProperty(audit => audit.RunId, (RunId?)null), cancellationToken);
        await db.ProjectFeedback.IgnoreQueryFilters()
            .Where(feedback => feedback.RunId != null && expired.Contains(feedback.RunId.Value))
            .ExecuteUpdateAsync(update => update.SetProperty(feedback => feedback.RunId, (RunId?)null), cancellationToken);
        await db.RunSteps.IgnoreQueryFilters().Where(step => expired.Contains(step.RunId)).ExecuteDeleteAsync(cancellationToken);
        int deleted = await runs.Where(run => run.StartedAt < metadataCutoff).ExecuteDeleteAsync(cancellationToken);

        return string.Create(CultureInfo.InvariantCulture,
            $"Deleted {payloads} step payloads older than {options.Value.PayloadRetention.TotalDays:0} days and {deleted} runs older than {options.Value.MetadataRetention.TotalDays:0} days.");
    }
}
