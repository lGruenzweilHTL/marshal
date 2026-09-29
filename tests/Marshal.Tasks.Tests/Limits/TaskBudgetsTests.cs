using Marshal.Abstractions;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Tasks.Limits;
using Marshal.Tasks.Store;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Tasks.Tests.Limits;

public sealed class TaskBudgetsTests : IAsyncLifetime
{
    // 2026-09-29 10:00 UTC = 12:00 in Vienna.
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 0, 0, TimeSpan.Zero));
    private readonly FakeSpending _spending = new();
    private readonly FakeTemplateCatalog _templates = new();
    private readonly TaskOptions _options = new()
    {
        DefaultBudget = new TaskBudgetOptions { Tokens = 100_000, Cost = 0.5m, Steps = 40, WallTime = TimeSpan.FromMinutes(30), Runs = 10 },
        DailyCostCap = 2m,
    };

    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Allowance_RemainingOfTheTaskWithDefaults()
    {
        TaskRecord task = await CreateAsync(new TaskBudget(Cost: 0.2m));
        task = await SpendAsync(task, new TaskSpend(Tokens: 1000, Cost: 0.05m, Steps: 4, WallTime: TimeSpan.FromMinutes(5), Runs: 1));

        RunLimits limits = (await Budgets().AllowanceAsync(task, Ct)).Value;

        Assert.Equal(new RunLimits(0.15m, 99_000, 36, TimeSpan.FromMinutes(25)), limits);
    }

    [Fact]
    public async Task Allowance_ChildDrawsFromParent()
    {
        TaskRecord parent = await CreateAsync(new TaskBudget(Cost: 0.3m));
        parent = await SpendAsync(parent, new TaskSpend(Cost: 0.25m, Runs: 1));
        TaskRecord child = await CreateAsync(new TaskBudget(Cost: 1m), parent.Id);

        RunLimits limits = (await Budgets().AllowanceAsync(child, Ct)).Value;

        Assert.Equal(0.05m, limits.MaxCost);
    }

    [Fact]
    public async Task Allowance_RunsUsedUp_BudgetExceeded()
    {
        TaskRecord task = await CreateAsync(new TaskBudget(Runs: 2));
        task = await SpendAsync(task, new TaskSpend(Runs: 2));

        Result<RunLimits> allowance = await Budgets().AllowanceAsync(task, Ct);

        Assert.Equal("tasks.budget_exceeded", allowance.Error.Code);
    }

    [Fact]
    public async Task Allowance_ParentCostUsedUp_BudgetExceeded()
    {
        TaskRecord parent = await CreateAsync(new TaskBudget(Cost: 0.1m));
        parent = await SpendAsync(parent, new TaskSpend(Cost: 0.1m));
        TaskRecord child = await CreateAsync(new TaskBudget(), parent.Id);

        Assert.Equal("tasks.budget_exceeded", (await Budgets().AllowanceAsync(child, Ct)).Error.Code);
    }

    [Fact]
    public async Task Allowance_DailyCapLimitsCostAndStopsRunsWhenReached()
    {
        TaskRecord task = await CreateAsync(new TaskBudget());
        _spending.Cost = 1.8m;

        RunLimits limits = (await Budgets().AllowanceAsync(task, Ct)).Value;
        _spending.Cost = 2m;
        Result<RunLimits> reached = await Budgets().AllowanceAsync(task, Ct);

        Assert.Equal(0.2m, limits.MaxCost);
        Assert.Equal("tasks.daily_cap", reached.Error.Code);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 22, 0, 0, TimeSpan.Zero), _spending.Since);
    }

    [Fact]
    public async Task NextDay_StartOfTheOwnersNextLocalDay()
    {
        Assert.Equal(new DateTimeOffset(2026, 9, 29, 22, 0, 0, TimeSpan.Zero), await Budgets().NextDayAsync(OwnerA, Ct));
    }

    [Fact]
    public async Task DrawDown_AddsSpendToEveryAncestor()
    {
        TaskRecord root = await CreateAsync(new TaskBudget());
        TaskRecord parent = await CreateAsync(new TaskBudget(), root.Id);
        TaskRecord child = await CreateAsync(new TaskBudget(), parent.Id);

        await Budgets().DrawDownAsync(child, new TaskSpend(Tokens: 10, Cost: 0.01m, Steps: 1, WallTime: TimeSpan.FromSeconds(3), Runs: 1), Ct);

        Assert.Equal(new TaskSpend(10, 0.01m, 1, TimeSpan.FromSeconds(3), 1), (await Store().GetAsync(parent.Id, Ct)).Value.Spent);
        Assert.Equal(new TaskSpend(10, 0.01m, 1, TimeSpan.FromSeconds(3), 1), (await Store().GetAsync(root.Id, Ct)).Value.Spent);
        Assert.Equal(TaskSpend.Zero, (await Store().GetAsync(child.Id, Ct)).Value.Spent);
    }

    [Fact]
    public async Task Wakeup_SoonerThanTheMinimum_ValidationError()
    {
        TaskRecord task = await CreateAsync(new TaskBudget());

        Result tooSoon = Budgets().CheckWakeup(task, _time.GetUtcNow().AddMinutes(1));

        Assert.Equal(("tasks.invalid", ErrorType.Validation), (tooSoon.Error.Code, tooSoon.Error.Type));
        Assert.True(Budgets().CheckWakeup(task, _time.GetUtcNow().AddMinutes(5)).IsSuccess);
    }

    [Fact]
    public async Task Wakeup_TemplateMinimumOverridesTheDefault()
    {
        _templates.Items.Add(FakeTemplateCatalog.Template("watcher") with { MinWakeupInterval = TimeSpan.FromMinutes(1) });
        TaskRecord task = (await Store().CreateAsync(new NewTask { Goal = "Watch.", Trigger = TaskTrigger.Now(), Template = "watcher" }, Ct)).Value;

        Assert.True(Budgets().CheckWakeup(task, _time.GetUtcNow().AddMinutes(1)).IsSuccess);
    }

    private TaskBudgets Budgets() => new(Store(), _spending, new ViennaProfiles(), _templates, Options.Create(_options), _time);

    private EfTaskStore Store() => new(_database.Factory, _database.Accessor, new ViennaProfiles(), Options.Create(_options), _time);

    private async Task<TaskRecord> CreateAsync(TaskBudget budget, TaskId? parent = null) =>
        (await Store().CreateAsync(new NewTask { Goal = "Do it.", Trigger = TaskTrigger.Now(), Budget = budget, ParentId = parent }, Ct)).Value;

    private async Task<TaskRecord> SpendAsync(TaskRecord task, TaskSpend spend) =>
        (await Store().UpdateAsync(task with { Spent = spend }, Ct)).Value;

    private sealed class FakeSpending : ITaskSpending
    {
        public decimal Cost { get; set; }

        public DateTimeOffset? Since { get; private set; }

        public Task<decimal> GetCostSinceAsync(DateTimeOffset since, CancellationToken cancellationToken)
        {
            Since = since;
            return Task.FromResult(Cost);
        }
    }
}
