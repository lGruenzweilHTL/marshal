namespace Marshal.Abstractions.Evals;

/// <summary>Replays one eval case against a candidate configuration without executing any tool.</summary>
public interface IEvalReplayer
{
    Task<ReplayResult> ReplayAsync(EvalCase evalCase, CandidateConfig candidate, CancellationToken cancellationToken);
}

/// <param name="Calls">The candidate's tool calls in order.</param>
/// <param name="Reply">The candidate's reply; null when it failed.</param>
/// <param name="Cost">Model cost of the replay.</param>
/// <param name="Error">Why the replay failed (error, limit), or null.</param>
public sealed record ReplayResult(IReadOnlyList<ReplayedCall> Calls, string? Reply, decimal Cost, string? Error)
{
    /// <summary>A call got "no recording".</summary>
    public bool Diverged => Calls.Any(call => !call.Recorded);
}

/// <summary>Scores a replayed reply against the recorded one and the case's note (0 to 1).</summary>
public interface IReplyJudge
{
    Task<JudgeScore> ScoreAsync(EvalCase evalCase, string reply, string judgeModel, CancellationToken cancellationToken);
}

/// <param name="Score">0 (bad) to 1 (good); null when the judge gave no score.</param>
/// <param name="Cost">Model cost of the judge calls.</param>
public sealed record JudgeScore(double? Score, decimal Cost);
