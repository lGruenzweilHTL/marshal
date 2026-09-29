using System.Collections.Concurrent;
using System.Threading.Channels;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tasks;
using Marshal.Core.Agent;
using Marshal.Tasks.Limits;
using Marshal.Tasks.Running;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Marshal.Tasks.Scheduling;

/// <summary>
/// Runs due tasks. One loop sleeps until the earliest <c>next_run_at</c> (or a <see cref="Wake"/>), then queues the
/// due tasks in run-time order; <see cref="TaskOptions.Workers"/> workers claim each task by moving it to
/// <c>running</c> with its row version, so a task is never run twice, run it as its owner and apply the outcome.
/// Tasks left <c>running</c> by a crash become <c>pending</c> again at startup.
/// </summary>
public sealed partial class BackgroundTaskScheduler(
    ITaskStore store,
    IEnumerable<ITaskRunner> runners,
    TaskBudgets budgets,
    IExecutionContextAccessor executionContext,
    IUserProfileProvider profiles,
    IOptions<TaskOptions> options,
    TimeProvider time,
    ILogger<BackgroundTaskScheduler> logger) : BackgroundService, ITaskScheduler
{
    private const int DueBatch = 100;
    private const int ApplyAttempts = 5;
    private const string RestartNote = "The previous run was interrupted by a restart and did not finish.";

    // Even without a wake, the loop re-reads the store this often (guards against missed signals and clock jumps).
    private static readonly TimeSpan _maxSleep = TimeSpan.FromMinutes(1);

    private readonly Dictionary<TaskKind, ITaskRunner> _runners = runners.ToDictionary(runner => runner.Kind);
    private readonly Channel<OwnedTask> _queue = Channel.CreateBounded<OwnedTask>(new BoundedChannelOptions(DueBatch) { SingleReader = false, SingleWriter = true });
    private readonly ConcurrentDictionary<TaskId, byte> _queued = new();
    private readonly SemaphoreSlim _signal = new(0, 1);
    private volatile bool _sleeping;
    private int _busy;
    private int _idleWorkers;

    /// <summary>True while the loop sleeps and no worker is claiming or finishing a task (for tests).</summary>
    internal bool IsIdle =>
        _sleeping && Volatile.Read(ref _busy) == 0 && (_queue.Reader.Count == 0 || Volatile.Read(ref _idleWorkers) == 0);

    public void Wake()
    {
        try
        {
            if (_signal.CurrentCount == 0)
                _signal.Release();
        }
        catch (SemaphoreFullException)
        {
            // Already signalled.
        }
    }

    public override void Dispose()
    {
        base.Dispose();
        _signal.Dispose();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RecoverInterruptedAsync(stoppingToken);
        Task[] workers = [.. Enumerable.Range(0, Math.Max(1, options.Value.Workers)).Select(_ => Task.Run(() => WorkAsync(stoppingToken), stoppingToken))];
        try
        {
            await LoopAsync(stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }

        _queue.Writer.TryComplete();
        try
        {
            await Task.WhenAll(workers);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task LoopAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            DateTimeOffset now = time.GetUtcNow();
            TimeSpan sleep = _maxSleep;
            try
            {
                foreach (OwnedTask due in await store.GetDueAsync(now, DueBatch, stoppingToken))
                {
                    if (_queued.TryAdd(due.Id, 0))
                        await _queue.Writer.WriteAsync(due, stoppingToken);
                }

                // When the earliest task is already due it is queued; the workers wake the loop when they finish.
                DateTimeOffset? next = await store.GetNextRunAtAsync(stoppingToken);
                if (next > now && next - now < sleep)
                    sleep = next.Value - now;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogLoopFailed(exception);
            }

            await SleepAsync(sleep, stoppingToken);
        }
    }

    private async Task SleepAsync(TimeSpan duration, CancellationToken stoppingToken)
    {
        using var sleeping = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        var timer = Task.Delay(duration, time, sleeping.Token);
        Task signal = _signal.WaitAsync(sleeping.Token);
        _sleeping = true;
        try
        {
            await Task.WhenAny(timer, signal);
        }
        finally
        {
            _sleeping = false;
            await sleeping.CancelAsync();
        }

        stoppingToken.ThrowIfCancellationRequested();
    }

    private async Task WorkAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            Interlocked.Increment(ref _idleWorkers);
            bool more;
            try
            {
                more = await _queue.Reader.WaitToReadAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                Interlocked.Decrement(ref _idleWorkers);
            }

            if (!more)
                return;
            if (!_queue.Reader.TryRead(out OwnedTask? due))
                continue;

            Interlocked.Increment(ref _busy);
            try
            {
                using (executionContext.Use(SystemContext(due.OwnerId)))
                    await ProcessAsync(due.Id, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
            {
                LogProcessFailed(due.Id, exception);
            }
            finally
            {
                _queued.TryRemove(due.Id, out _);
                Interlocked.Decrement(ref _busy);
                Wake();
            }
        }
    }

    private async Task ProcessAsync(TaskId id, CancellationToken stoppingToken)
    {
        Result<TaskRecord> loaded = await store.GetAsync(id, stoppingToken);
        if (loaded.IsFailure)
            return;
        TaskRecord task = loaded.Value;
        if (task.Status is not (AgentTaskStatus.Pending or AgentTaskStatus.Waiting) || task.NextRunAt is null || task.NextRunAt > time.GetUtcNow())
            return;

        Result<RunLimits> allowance = await budgets.AllowanceAsync(task, stoppingToken);
        if (allowance.IsFailure)
        {
            await RefuseAsync(task, allowance.Error, stoppingToken);
            return;
        }

        Result<TaskRecord> claimed = await store.UpdateAsync(task with { Status = AgentTaskStatus.Running, StatusReason = null, NextRunAt = null }, stoppingToken);
        if (claimed.IsFailure)
            return; // Someone else claimed or changed it first.

        long started = time.GetTimestamp();
        TaskRunOutcome outcome = await RunAsync(claimed.Value, allowance.Value, stoppingToken);
        TaskSpend spend = outcome.Spend with { WallTime = time.GetElapsedTime(started), Runs = 1 };
        TaskRecord? finished = await ApplyAsync(id, outcome, spend, stoppingToken);
        await budgets.DrawDownAsync(claimed.Value, spend, stoppingToken);
        if (finished is { Status: AgentTaskStatus.Done or AgentTaskStatus.Waiting } && outcome.End == TaskRunEnd.Done)
            await ReleaseDependentsAsync(id, stoppingToken);
    }

    // Over budget: the task fails. Daily cap reached: the task waits for the owner's next day.
    private async Task RefuseAsync(TaskRecord task, Error refusal, CancellationToken stoppingToken)
    {
        TaskRecord refused = refusal == TaskErrors.DailyCap()
            ? task with { StatusReason = "daily_cap", NextRunAt = await budgets.NextDayAsync(task.OwnerId, stoppingToken) }
            : task with { Status = AgentTaskStatus.Failed, StatusReason = "budget_exceeded", NextRunAt = null };
        await store.UpdateAsync(refused, stoppingToken);
        LogRefused(task.Id, refused.StatusReason!);
    }

    private async Task<TaskRunOutcome> RunAsync(TaskRecord task, RunLimits limits, CancellationToken stoppingToken)
    {
        if (!_runners.TryGetValue(task.Kind, out ITaskRunner? runner))
            return TaskRunOutcome.Failed("no_runner");

        Interlocked.Decrement(ref _busy);
        try
        {
            return await runner.RunAsync(task, limits, stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !stoppingToken.IsCancellationRequested)
        {
            LogRunFailed(task.Id, exception);
            return TaskRunOutcome.Failed("error");
        }
        finally
        {
            Interlocked.Increment(ref _busy);
        }
    }

    // Tools may have changed the task during the run (notes, its own cancel), so the outcome is applied to a fresh copy.
    private async Task<TaskRecord?> ApplyAsync(TaskId id, TaskRunOutcome outcome, TaskSpend spend, CancellationToken stoppingToken)
    {
        for (int attempt = 0; attempt < ApplyAttempts; attempt++)
        {
            Result<TaskRecord> current = await store.GetAsync(id, stoppingToken);
            if (current.IsFailure || current.Value.Status != AgentTaskStatus.Running)
                return null; // Cancelled (or otherwise ended) while running: keep that.

            TaskRecord task = current.Value;
            DateTimeOffset now = time.GetUtcNow();
            TaskRecord updated = task with
            {
                TriggerPayload = null,
                Notes = outcome.Notes ?? task.Notes,
                Spent = task.Spent.Add(spend),
            };
            updated = outcome.End switch
            {
                TaskRunEnd.Done => await RearmAsync(updated with { Result = outcome.Result, ResultAt = now }, now, stoppingToken),
                TaskRunEnd.Waiting => updated with { Status = AgentTaskStatus.Waiting, NextRunAt = outcome.WakeAt },
                _ => updated with { Status = AgentTaskStatus.Failed, StatusReason = outcome.Reason, NextRunAt = null },
            };

            Result<TaskRecord> saved = await store.UpdateAsync(updated, stoppingToken);
            if (saved.IsSuccess)
                return saved.Value;
        }

        LogApplyGaveUp(id);
        return null;
    }

    // Recurring triggers (cron, event) wait for their next occurrence, which starts a new activation with a fresh
    // budget; one-shot triggers are done.
    private async Task<TaskRecord> RearmAsync(TaskRecord task, DateTimeOffset now, CancellationToken cancellationToken) => task.Trigger.Kind switch
    {
        TaskTriggerKind.Cron => task with
        {
            Status = AgentTaskStatus.Waiting,
            NextRunAt = TaskTriggers.NextRun(task.Trigger, now, await profiles.TimeZoneAsync(task.OwnerId, cancellationToken)),
            Spent = TaskSpend.Zero,
        },
        TaskTriggerKind.Event => task with { Status = AgentTaskStatus.Waiting, NextRunAt = null, Spent = TaskSpend.Zero },
        _ => task with { Status = AgentTaskStatus.Done, NextRunAt = null },
    };

    private async Task ReleaseDependentsAsync(TaskId finished, CancellationToken stoppingToken)
    {
        var after = TaskTrigger.After(finished);
        IReadOnlyList<TaskRecord> waiting = await store.ListAsync(new TaskFilter([AgentTaskStatus.Pending, AgentTaskStatus.Waiting]), stoppingToken);
        foreach (TaskRecord dependent in waiting.Where(task => task.Trigger == after && task.NextRunAt is null))
            await store.UpdateAsync(dependent with { NextRunAt = time.GetUtcNow() }, stoppingToken);
    }

    private async Task RecoverInterruptedAsync(CancellationToken stoppingToken)
    {
        foreach (OwnedTask interrupted in await store.GetRunningAsync(stoppingToken))
        {
            using (executionContext.Use(SystemContext(interrupted.OwnerId)))
            {
                Result<TaskRecord> task = await store.GetAsync(interrupted.Id, stoppingToken);
                if (task.IsFailure)
                    continue;

                string notes = string.IsNullOrEmpty(task.Value.Notes) ? RestartNote : $"{task.Value.Notes}\n{RestartNote}";
                await store.UpdateAsync(task.Value with { Status = AgentTaskStatus.Pending, StatusReason = "restarted", NextRunAt = time.GetUtcNow(), Notes = notes }, stoppingToken);
                LogRecovered(interrupted.Id);
            }
        }
    }

    private static MarshalExecutionContext SystemContext(OwnerId owner) =>
        new() { OwnerId = owner, Source = SessionSource.Task, IsInteractive = false };

    [LoggerMessage(Level = LogLevel.Error, Message = "Task scheduler loop failed")]
    private partial void LogLoopFailed(Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Processing task {TaskId} failed")]
    private partial void LogProcessFailed(TaskId taskId, Exception exception);

    [LoggerMessage(Level = LogLevel.Error, Message = "Run of task {TaskId} failed")]
    private partial void LogRunFailed(TaskId taskId, Exception exception);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Outcome of task {TaskId} could not be saved after repeated conflicts")]
    private partial void LogApplyGaveUp(TaskId taskId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Task {TaskId} not started: {Reason}")]
    private partial void LogRefused(TaskId taskId, string reason);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Task {TaskId} was running at startup; it is pending again")]
    private partial void LogRecovered(TaskId taskId);
}
