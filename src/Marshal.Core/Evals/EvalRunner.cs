using System.Collections.Concurrent;
using System.Threading.Channels;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Runs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Marshal.Core.Evals;

/// <summary>Starts and cancels eval runs; the worker replays them.</summary>
public interface IEvalRunControl
{
    /// <summary>Queues a stored run of <paramref name="owner"/>.</summary>
    void Enqueue(EvalRunId run, OwnerId owner);

    /// <summary>Cancels a queued or running run; the case being replayed stops too.</summary>
    Task<bool> CancelAsync(EvalRunId run, CancellationToken cancellationToken);
}

/// <summary>
/// Replays queued eval runs one case at a time in the background: replay, judge (when the candidate names a judge
/// model), compare, store. Each case draws from the owner's daily cost cap; a run stops as <c>failed</c> when the cap
/// is used up, and as <c>cancelled</c> when an admin cancels it.
/// </summary>
public sealed partial class EvalRunner(
    IEvalRunStore runs,
    IEvalCaseStore cases,
    IEvalReplayer replayer,
    IReplyJudge judge,
    IExecutionContextAccessor executionContext,
    ILogger<EvalRunner> logger,
    IDailyCostCap? cap = null) : BackgroundService, IEvalRunControl
{
    private readonly Channel<(EvalRunId Run, OwnerId Owner)> _queue = Channel.CreateUnbounded<(EvalRunId, OwnerId)>();
    private readonly ConcurrentDictionary<EvalRunId, CancellationTokenSource> _active = new();

    public void Enqueue(EvalRunId run, OwnerId owner)
    {
        _active[run] = new CancellationTokenSource();
        _queue.Writer.TryWrite((run, owner));
    }

    public async Task<bool> CancelAsync(EvalRunId run, CancellationToken cancellationToken)
    {
        bool changed = await runs.SetStatusAsync(run, EvalRunStatus.Cancelled, null, cancellationToken);
        if (_active.TryRemove(run, out CancellationTokenSource? source))
        {
            await source.CancelAsync();
            source.Dispose();
        }

        return changed;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach ((EvalRunId run, OwnerId owner) in _queue.Reader.ReadAllAsync(stoppingToken))
        {
            if (!_active.TryGetValue(run, out CancellationTokenSource? source))
                continue;

            using var linked = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, source.Token);
            try
            {
                await RunAsync(run, owner, linked.Token);
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                LogCancelled(run.Value);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                LogFailed(exception, run.Value);
                using IDisposable scope = executionContext.Use(Context(owner));
                await runs.SetStatusAsync(run, EvalRunStatus.Failed, exception.Message, CancellationToken.None);
            }
            finally
            {
                if (_active.TryRemove(run, out CancellationTokenSource? done))
                    done.Dispose();
            }
        }
    }

    /// <summary>Replays every case of the run in order (also used directly by tests).</summary>
    public async Task RunAsync(EvalRunId id, OwnerId owner, CancellationToken cancellationToken)
    {
        using IDisposable scope = executionContext.Use(Context(owner));
        if (await runs.GetAsync(id, cancellationToken) is not { Status: EvalRunStatus.Queued } run)
            return;

        await runs.SetStatusAsync(id, EvalRunStatus.Running, null, cancellationToken);
        foreach (EvalCaseId caseId in run.CaseIds)
        {
            if (await runs.GetAsync(id, cancellationToken) is not { Status: EvalRunStatus.Running })
                return;
            if (cap is not null && await cap.LeftTodayAsync(owner, cancellationToken) <= 0)
            {
                await runs.SetStatusAsync(id, EvalRunStatus.Failed, "The daily cost cap is used up.", cancellationToken);
                return;
            }

            await runs.AddResultAsync(id, await ReplayCaseAsync(caseId, run.Candidate, cancellationToken), cancellationToken);
        }

        await runs.SetStatusAsync(id, EvalRunStatus.Completed, null, cancellationToken);
    }

    private async Task<EvalCaseResult> ReplayCaseAsync(EvalCaseId caseId, CandidateConfig candidate, CancellationToken cancellationToken)
    {
        if (await cases.GetAsync(caseId, cancellationToken) is not { } evalCase)
            return new EvalCaseResult(caseId, EvalOutcome.Failed, [], null, null, 0, "The case was deleted.");

        ReplayResult replay = await replayer.ReplayAsync(evalCase, candidate, cancellationToken);
        JudgeScore? score = candidate.JudgeModel is { } judgeModel && replay.Reply is { } reply && replay.Error is null
            ? await judge.ScoreAsync(evalCase, reply, judgeModel, cancellationToken)
            : null;
        return new EvalCaseResult(caseId, EvalComparison.Compare(evalCase, replay, score?.Score), replay.Calls, replay.Reply, score?.Score, replay.Cost + (score?.Cost ?? 0), replay.Error);
    }

    private static MarshalExecutionContext Context(OwnerId owner) => new() { OwnerId = owner, Source = SessionSource.Web, IsInteractive = false };

    [LoggerMessage(Level = LogLevel.Information, Message = "Eval run {Run} cancelled")]
    private partial void LogCancelled(Guid run);

    [LoggerMessage(Level = LogLevel.Error, Message = "Eval run {Run} failed")]
    private partial void LogFailed(Exception exception, Guid run);
}
