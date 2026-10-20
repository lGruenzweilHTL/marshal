using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Identity;

namespace Marshal.Data.Evals;

/// <summary>An eval run (<c>eval_runs</c>): a candidate configuration replayed over selected cases.</summary>
public sealed class EvalRunRow : IOwned
{
    public required EvalRunId Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    /// <summary>JSON of the <see cref="CandidateConfig"/>.</summary>
    public required string Candidate { get; init; }

    /// <summary>JSON array of the case ids, in replay order.</summary>
    public required string CaseIds { get; init; }

    public required string StartedBy { get; init; }

    public EvalRunStatus Status { get; set; }

    public int Pass { get; set; }

    public int Changed { get; set; }

    public int Diverged { get; set; }

    public int Failed { get; set; }

    public decimal Cost { get; set; }

    public decimal EstimatedCost { get; init; }

    public string? Error { get; set; }

    public required DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? EndedAt { get; set; }
}

/// <summary>One replayed case of an eval run (<c>eval_results</c>).</summary>
public sealed class EvalResultRow : IOwned
{
    public required Guid Id { get; init; }

    public required OwnerId OwnerId { get; init; }

    public required EvalRunId RunId { get; init; }

    /// <summary>The case (no foreign key: a deleted case keeps its results in the report).</summary>
    public required EvalCaseId CaseId { get; init; }

    public required EvalOutcome Outcome { get; init; }

    /// <summary>JSON array of <see cref="ReplayedCall"/>.</summary>
    public required string Calls { get; init; }

    public string? Reply { get; init; }

    public double? JudgeScore { get; init; }

    public decimal Cost { get; init; }

    public string? Error { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
