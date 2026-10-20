using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Marshal.Abstractions.Tasks;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions;
using Marshal.Core.Agent;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Limits;

/// <summary>
/// Server-side budget enforcement. A run may use what is left of its task's budget and of every ancestor's budget
/// (children draw from their parents), and no more than what is left of the owner's daily cost cap. Unset task
/// limits fall back to <see cref="TaskOptions.DefaultBudget"/>.
/// </summary>
public sealed class TaskBudgets(
    ITaskStore store,
    ITaskSpending spending,
    IUserProfileProvider profiles,
    ITemplateCatalog templates,
    IOptions<TaskOptions> options,
    TimeProvider time) : IDailyCostCap
{
    private const int DrawDownAttempts = 5;

    /// <summary>
    /// The limits for the task's next run, or <c>tasks.budget_exceeded</c> / <c>tasks.daily_cap</c> when it may not run.
    /// </summary>
    public async Task<Result<RunLimits>> AllowanceAsync(TaskRecord task, CancellationToken cancellationToken)
    {
        var remaining = Remaining.Of(task, options.Value.DefaultBudget);
        TaskId? ancestor = task.ParentId;
        while (ancestor is TaskId id && (await store.GetAsync(id, cancellationToken)) is { IsSuccess: true } parent)
        {
            remaining = remaining.Min(Remaining.Of(parent.Value, options.Value.DefaultBudget));
            ancestor = parent.Value.ParentId;
        }

        if (remaining.IsUsedUp)
            return TaskErrors.BudgetExceeded();

        decimal leftToday = await LeftTodayAsync(task.OwnerId, cancellationToken);
        if (leftToday <= 0)
            return TaskErrors.DailyCap();

        return new RunLimits(Math.Min(remaining.Cost, leftToday), remaining.Tokens, remaining.Steps, remaining.WallTime);
    }

    /// <summary>What is left of the daily cost cap today; AI tasks draw from the same cap.</summary>
    public async Task<decimal> LeftTodayAsync(OwnerId owner, CancellationToken cancellationToken)
    {
        DateTimeOffset today = await StartOfDayAsync(owner, 0, cancellationToken);
        return options.Value.DailyCostCap - await spending.GetCostSinceAsync(today, cancellationToken);
    }

    /// <summary>Adds a run's spend to every ancestor of <paramref name="task"/> (the task itself is updated by the scheduler).</summary>
    public async Task DrawDownAsync(TaskRecord task, TaskSpend spend, CancellationToken cancellationToken)
    {
        TaskId? ancestor = task.ParentId;
        while (ancestor is TaskId id)
        {
            ancestor = null;
            for (int attempt = 0; attempt < DrawDownAttempts; attempt++)
            {
                Result<TaskRecord> parent = await store.GetAsync(id, cancellationToken);
                if (parent.IsFailure)
                    break;

                ancestor = parent.Value.ParentId;
                if ((await store.UpdateAsync(parent.Value with { Spent = parent.Value.Spent.Add(spend) }, cancellationToken)).IsSuccess)
                    break;
            }
        }
    }

    /// <summary>Fails when <paramref name="wakeAt"/> is closer than the minimum wakeup interval of the task's template.</summary>
    public Result CheckWakeup(TaskRecord task, DateTimeOffset wakeAt)
    {
        TimeSpan minimum = MinWakeupInterval(task.Template);
        return wakeAt - time.GetUtcNow() < minimum
            ? TaskErrors.Invalid($"A wakeup must be at least {minimum.TotalMinutes:0} minutes ahead.")
            : Result.Success();
    }

    /// <summary>Start of the owner's next local day (when the daily cap resets).</summary>
    public Task<DateTimeOffset> NextDayAsync(OwnerId owner, CancellationToken cancellationToken) => StartOfDayAsync(owner, 1, cancellationToken);

    private TimeSpan MinWakeupInterval(string? template) =>
        (template is null ? null : templates.Find(template)?.MinWakeupInterval) ?? options.Value.MinWakeupInterval;

    private async Task<DateTimeOffset> StartOfDayAsync(OwnerId owner, int days, CancellationToken cancellationToken)
    {
        TimeZoneInfo zone = await profiles.TimeZoneAsync(owner, cancellationToken);
        DateTime local = TimeZoneInfo.ConvertTime(time.GetUtcNow(), zone).Date.AddDays(days);
        return new DateTimeOffset(local, zone.GetUtcOffset(local)).ToUniversalTime();
    }

    private readonly record struct Remaining(decimal Cost, long? Tokens, int Steps, TimeSpan WallTime, int Runs)
    {
        public bool IsUsedUp => Cost <= 0 || Tokens <= 0 || Steps <= 0 || WallTime <= TimeSpan.Zero || Runs <= 0;

        public static Remaining Of(TaskRecord task, TaskBudgetOptions defaults)
        {
            TaskBudget budget = task.Budget;
            TaskSpend spent = task.Spent;
            return new(
                (budget.Cost ?? defaults.Cost) - spent.Cost,
                (budget.Tokens ?? defaults.Tokens) - spent.Tokens,
                (budget.Steps ?? defaults.Steps) - spent.Steps,
                (budget.WallTime ?? defaults.WallTime) - spent.WallTime,
                (budget.Runs ?? defaults.Runs) - spent.Runs);
        }

        public Remaining Min(Remaining other) => new(
            Math.Min(Cost, other.Cost),
            Tokens is null ? other.Tokens : other.Tokens is null ? Tokens : Math.Min(Tokens.Value, other.Tokens.Value),
            Math.Min(Steps, other.Steps),
            WallTime < other.WallTime ? WallTime : other.WallTime,
            Math.Min(Runs, other.Runs));
    }
}
