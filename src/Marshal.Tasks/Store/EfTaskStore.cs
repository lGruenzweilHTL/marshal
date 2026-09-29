using System.Globalization;
using System.Text.RegularExpressions;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Data;
using Marshal.Data.Tasks;
using Marshal.Tasks.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Tasks.Store;

/// <summary>
/// <see cref="ITaskStore"/> on the <c>agent_tasks</c> table. Owner-scoped through the context's query filter; only
/// the scheduler's due queries read across owners.
/// </summary>
public sealed partial class EfTaskStore(
    IDbContextFactory<MarshalDbContext> contexts,
    IExecutionContextAccessor executionContext,
    IUserProfileProvider profiles,
    TimeProvider timeProvider) : ITaskStore
{
    private const int MaxGoalLength = 8000;

    private static readonly AgentTaskStatus[] _schedulable = [AgentTaskStatus.Pending, AgentTaskStatus.Waiting];

    public async Task<Result<TaskRecord>> CreateAsync(NewTask task, CancellationToken cancellationToken)
    {
        OwnerId owner = CurrentOwner();
        Result<string?> name = NormalizeName(task.Name);
        if (name.IsFailure)
            return name.Error;
        Result valid = Validate(task.Goal, task.Trigger, task.Budget);
        if (valid.IsFailure)
            return valid.Error;

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        Result afterValid = await ValidateAfterAsync(db, task.Trigger, cancellationToken);
        if (afterValid.IsFailure)
            return afterValid.Error;

        int depth = 0;
        if (task.ParentId is TaskId parentId)
        {
            AgentTask? parent = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(row => row.Id == parentId, cancellationToken);
            if (parent is null)
                return TaskErrors.NotFound(parentId.ToString());
            depth = parent.Depth + 1;
        }

        if (name.Value is string taken && await db.AgentTasks.AnyAsync(row => row.Name == taken, cancellationToken))
            return TaskErrors.NameTaken(taken);

        DateTimeOffset now = timeProvider.GetUtcNow();
        var row = new AgentTask
        {
            Id = TaskId.New(),
            OwnerId = owner,
            ParentId = task.ParentId,
            CreatedBy = task.CreatedBy,
            Kind = task.Kind,
            Goal = task.Goal.Trim(),
            Job = task.Job,
            Template = task.Template,
            ProjectId = task.ProjectId,
            OriginSatelliteId = task.OriginSatelliteId,
            TriggerKind = task.Trigger.Kind,
            TriggerSpec = task.Trigger.Spec,
            NextRunAt = TaskTriggers.NextRun(task.Trigger, now, await profiles.TimeZoneAsync(owner, cancellationToken)),
            Notes = task.Notes,
            Budget = TaskJson.Serialize(task.Budget),
            Spent = TaskJson.Serialize(TaskSpend.Zero),
            Name = name.Value,
            Deliveries = TaskJson.Serialize(task.Deliveries),
            OverrideQuietHoursUntil = task.OverrideQuietHoursUntil,
            Depth = depth,
            CreatedAt = now,
            UpdatedAt = now,
        };
        db.AgentTasks.Add(row);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (name.Value is not null)
        {
            return TaskErrors.NameTaken(name.Value);
        }

        return ToRecord(row);
    }

    public async Task<Result<TaskRecord>> GetAsync(TaskId id, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        AgentTask? row = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        return row is null ? TaskErrors.NotFound(id.ToString()) : ToRecord(row);
    }

    public async Task<Result<TaskRecord>> FindAsync(string reference, CancellationToken cancellationToken)
    {
        if (TaskId.TryParse(reference.Trim(), CultureInfo.InvariantCulture, out TaskId id))
            return await GetAsync(id, cancellationToken);

        string name = reference.Trim().ToLowerInvariant();
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        AgentTask? row = await db.AgentTasks.AsNoTracking().SingleOrDefaultAsync(candidate => candidate.Name == name, cancellationToken);
        return row is null ? TaskErrors.NotFound(reference) : ToRecord(row);
    }

    public async Task<IReadOnlyList<TaskRecord>> ListAsync(TaskFilter filter, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        IQueryable<AgentTask> query = db.AgentTasks.AsNoTracking();
        if (filter.Statuses is { } statuses)
            query = query.Where(row => statuses.Contains(row.Status));
        if (filter.ParentId is TaskId parent)
            query = query.Where(row => row.ParentId == parent);

        List<AgentTask> rows = await query.ToListAsync(cancellationToken);
        return [.. rows.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id.Value).Select(ToRecord)];
    }

    public async Task<Result<TaskRecord>> UpdateAsync(TaskRecord task, CancellationToken cancellationToken)
    {
        Result<string?> name = NormalizeName(task.Name);
        if (name.IsFailure)
            return name.Error;
        Result valid = Validate(task.Goal, task.Trigger, budget: null);
        if (valid.IsFailure)
            return valid.Error;

        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        AgentTask? row = await db.AgentTasks.SingleOrDefaultAsync(candidate => candidate.Id == task.Id, cancellationToken);
        if (row is null)
            return TaskErrors.NotFound(task.Id.ToString());
        Result afterValid = await ValidateAfterAsync(db, task.Trigger, cancellationToken);
        if (afterValid.IsFailure)
            return afterValid.Error;
        if (name.Value is string taken && taken != row.Name && await db.AgentTasks.AnyAsync(other => other.Name == taken, cancellationToken))
            return TaskErrors.NameTaken(taken);

        // The caller's version is what the database must still hold; the context increments it on save.
        db.Entry(row).Property(entity => entity.RowVersion).OriginalValue = task.RowVersion;
        row.RowVersion = task.RowVersion;
        row.Goal = task.Goal.Trim();
        row.Template = task.Template;
        row.TriggerKind = task.Trigger.Kind;
        row.TriggerSpec = task.Trigger.Spec;
        row.TriggerPayload = task.TriggerPayload;
        row.Status = task.Status;
        row.StatusReason = task.StatusReason;
        row.NextRunAt = task.NextRunAt;
        row.Notes = task.Notes;
        row.Spent = TaskJson.Serialize(task.Spent);
        row.Name = name.Value;
        row.Deliveries = TaskJson.Serialize(task.Deliveries);
        row.OverrideQuietHoursUntil = task.OverrideQuietHoursUntil;
        row.ResultSpeech = task.Result?.Speech;
        row.ResultBody = task.Result?.Body;
        row.ResultAt = task.ResultAt;
        row.UpdatedAt = timeProvider.GetUtcNow();
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return TaskErrors.Conflict();
        }
        catch (DbUpdateException) when (name.Value is not null)
        {
            return TaskErrors.NameTaken(name.Value);
        }

        return ToRecord(row);
    }

    public async Task<IReadOnlyList<OwnedTask>> GetDueAsync(DateTimeOffset now, int limit, CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await Schedulable(db)
            .Where(row => row.NextRunAt <= now)
            .OrderBy(row => row.NextRunAt)
            .Take(limit)
            .Select(row => new OwnedTask(row.Id, row.OwnerId))
            .ToListAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> GetNextRunAtAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await Schedulable(db).OrderBy(row => row.NextRunAt).Select(row => row.NextRunAt).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<OwnedTask>> GetRunningAsync(CancellationToken cancellationToken)
    {
        await using MarshalDbContext db = await contexts.CreateDbContextAsync(cancellationToken);
        return await db.AgentTasks.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.Status == AgentTaskStatus.Running)
            .Select(row => new OwnedTask(row.Id, row.OwnerId))
            .ToListAsync(cancellationToken);
    }

    // The scheduler serves every owner; it only reads ids and owners here and loads each task as its owner.
    private static IQueryable<AgentTask> Schedulable(MarshalDbContext db) =>
        db.AgentTasks.IgnoreQueryFilters().AsNoTracking().Where(row => _schedulable.Contains(row.Status) && row.NextRunAt != null);

    private OwnerId CurrentOwner() =>
        executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Tasks accessed without an execution context.");

    private static Result Validate(string goal, TaskTrigger trigger, TaskBudget? budget)
    {
        if (string.IsNullOrWhiteSpace(goal))
            return TaskErrors.Invalid("A task needs a goal.");
        if (goal.Length > MaxGoalLength)
            return TaskErrors.Invalid($"A goal has at most {MaxGoalLength} characters.");
        if (budget is { } limits && (limits.Tokens < 0 || limits.Cost < 0 || limits.Steps < 0 || limits.WallTime < TimeSpan.Zero || limits.Runs < 0))
            return TaskErrors.Invalid("Budget limits cannot be negative.");
        return TaskTriggers.Validate(trigger);
    }

    // An `after` trigger must name one of the owner's own tasks; the query filter hides the others.
    private static async Task<Result> ValidateAfterAsync(MarshalDbContext db, TaskTrigger trigger, CancellationToken cancellationToken)
    {
        if (trigger.Kind != TaskTriggerKind.After)
            return Result.Success();
        var other = TaskId.Parse(trigger.Spec, CultureInfo.InvariantCulture);
        return await db.AgentTasks.AnyAsync(row => row.Id == other, cancellationToken)
            ? Result.Success()
            : TaskErrors.Invalid($"There is no task '{trigger.Spec}' to run after.");
    }

    private static Result<string?> NormalizeName(string? name)
    {
        if (name is null)
            return (string?)null;
        string normalized = name.Trim().ToLowerInvariant();
        if (!NamePattern().IsMatch(normalized) || Guid.TryParse(normalized, out _))
            return TaskErrors.Invalid($"'{name}' is not a valid task name (lowercase letters, digits, '-' and '_', at most 64 characters).");
        return normalized;
    }

    private static TaskRecord ToRecord(AgentTask row) => new()
    {
        Id = row.Id,
        OwnerId = row.OwnerId,
        ParentId = row.ParentId,
        CreatedBy = row.CreatedBy,
        Kind = row.Kind,
        Goal = row.Goal,
        Job = row.Job,
        Template = row.Template,
        ProjectId = row.ProjectId,
        OriginSatelliteId = row.OriginSatelliteId,
        Trigger = new TaskTrigger(row.TriggerKind, row.TriggerSpec),
        TriggerPayload = row.TriggerPayload,
        Status = row.Status,
        StatusReason = row.StatusReason,
        NextRunAt = row.NextRunAt,
        Notes = row.Notes,
        Budget = TaskJson.Deserialize<TaskBudget>(row.Budget),
        Spent = TaskJson.Deserialize<TaskSpend>(row.Spent),
        Name = row.Name,
        Deliveries = TaskJson.Deserialize<List<TaskDelivery>>(row.Deliveries),
        OverrideQuietHoursUntil = row.OverrideQuietHoursUntil,
        Result = row.ResultSpeech is not null && row.ResultBody is not null ? new TaskResult(row.ResultSpeech, row.ResultBody) : null,
        ResultAt = row.ResultAt,
        Depth = row.Depth,
        CreatedAt = row.CreatedAt,
        UpdatedAt = row.UpdatedAt,
        RowVersion = row.RowVersion,
    };

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex NamePattern();
}
