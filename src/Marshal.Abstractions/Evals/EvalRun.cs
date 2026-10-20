using Marshal.Abstractions.Models;

namespace Marshal.Abstractions.Evals;

/// <summary>The configuration a replay tests.</summary>
/// <param name="Models">Configured model key per tier; tiers left out use the current routing.</param>
/// <param name="Template">Agent template to run the cases with; null keeps <c>default</c>.</param>
/// <param name="BasePrompt">Replaces Marshal's fixed base instructions; null keeps them.</param>
/// <param name="JudgeModel">Configured model key that scores replies; null for no judge.</param>
public sealed record CandidateConfig(IReadOnlyDictionary<ModelTier, string> Models, string? Template = null, string? BasePrompt = null, string? JudgeModel = null);

/// <summary>Where an eval run is.</summary>
public enum EvalRunStatus
{
    Queued,
    Running,
    Completed,
    Cancelled,

    /// <summary>Stopped early: the daily cost cap ran out or the run broke.</summary>
    Failed,
}

/// <summary>How a replayed case compares with its recording.</summary>
public enum EvalOutcome
{
    /// <summary>Good case: same tools and arguments. Bad case: a different tool sequence.</summary>
    Pass,

    /// <summary>Same tools with other arguments or another reply; a bad case that repeats its recording.</summary>
    Changed,

    /// <summary>The candidate made a call that has no recording.</summary>
    Diverged,

    /// <summary>The replay failed (error, step or cost limit).</summary>
    Failed,
}

/// <summary>A tool call the candidate made in a replay.</summary>
/// <param name="Tool">Internal tool id.</param>
/// <param name="Arguments">Canonical arguments.</param>
/// <param name="Recorded">It matched a recorded call and got its result.</param>
public sealed record ReplayedCall(string Tool, string Arguments, bool Recorded);

/// <summary>The result of one case in an eval run.</summary>
public sealed record EvalCaseResult(
    EvalCaseId CaseId,
    EvalOutcome Outcome,
    IReadOnlyList<ReplayedCall> Calls,
    string? Reply,
    double? JudgeScore,
    decimal Cost,
    string? Error);

/// <summary>An eval run with its totals.</summary>
public sealed record EvalRunRecord(
    EvalRunId Id,
    CandidateConfig Candidate,
    EvalRunStatus Status,
    IReadOnlyList<EvalCaseId> CaseIds,
    string StartedBy,
    decimal EstimatedCost,
    DateTimeOffset CreatedAt)
{
    public int Pass { get; init; }

    public int Changed { get; init; }

    public int Diverged { get; init; }

    public int Failed { get; init; }

    public decimal Cost { get; init; }

    public DateTimeOffset? EndedAt { get; init; }

    public string? Error { get; init; }
}

/// <summary>The current owner's eval runs and their results; kept until deleted.</summary>
public interface IEvalRunStore
{
    Task<EvalRunRecord> CreateAsync(CandidateConfig candidate, IReadOnlyList<EvalCaseId> cases, string startedBy, decimal estimatedCost, CancellationToken cancellationToken);

    Task<IReadOnlyList<EvalRunRecord>> ListAsync(CancellationToken cancellationToken);

    Task<EvalRunRecord?> GetAsync(EvalRunId id, CancellationToken cancellationToken);

    Task<IReadOnlyList<EvalCaseResult>> ResultsAsync(EvalRunId id, CancellationToken cancellationToken);

    /// <summary>Stores a case result and adds it to the run's totals and cost.</summary>
    Task AddResultAsync(EvalRunId id, EvalCaseResult result, CancellationToken cancellationToken);

    /// <summary>Moves the run to <paramref name="status"/>; a cancelled or ended run stays as it is.</summary>
    Task<bool> SetStatusAsync(EvalRunId id, EvalRunStatus status, string? error, CancellationToken cancellationToken);

    Task<Result> DeleteAsync(EvalRunId id, CancellationToken cancellationToken);
}
