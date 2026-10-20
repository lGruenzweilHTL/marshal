using Marshal.Abstractions.Evals;

namespace Marshal.Core.Evals;

/// <summary>
/// Compares a replay with its case. The tool sequence (ids, then canonical arguments) is the primary signal; the
/// reply only decides between <see cref="EvalOutcome.Pass"/> and <see cref="EvalOutcome.Changed"/> for good cases:
/// the same text passes, another text passes only with a judge score of at least <see cref="JudgePassScore"/>.
/// </summary>
public static class EvalComparison
{
    /// <summary>A judge score at or above this counts a good case's different reply as a pass.</summary>
    public const double JudgePassScore = 0.7;

    public static EvalOutcome Compare(EvalCase evalCase, ReplayResult replay, double? judgeScore = null)
    {
        if (replay.Error is not null)
            return EvalOutcome.Failed;
        if (replay.Diverged)
            return EvalOutcome.Diverged;

        string[] recordedTools = [.. evalCase.ToolCalls.Select(call => call.Tool)];
        string[] replayedTools = [.. replay.Calls.Select(call => call.Tool)];
        bool sameTools = recordedTools.SequenceEqual(replayedTools, StringComparer.Ordinal);
        bool sameArguments = sameTools && evalCase.ToolCalls.Select(call => call.Arguments).SequenceEqual(replay.Calls.Select(call => call.Arguments), StringComparer.Ordinal);

        // A bad case passes when the candidate no longer does what was rated down.
        if (evalCase.Expectation == EvalExpectation.Bad)
            return sameTools ? EvalOutcome.Changed : EvalOutcome.Pass;

        if (!sameArguments)
            return EvalOutcome.Changed;

        if (string.Equals(Normalize(evalCase.Reply), Normalize(replay.Reply), StringComparison.Ordinal))
            return EvalOutcome.Pass;

        return judgeScore >= JudgePassScore ? EvalOutcome.Pass : EvalOutcome.Changed;
    }

    private static string Normalize(string? reply) => string.Join(' ', (reply ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
