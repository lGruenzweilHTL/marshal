using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Evals;

/// <summary>
/// Estimates what an eval run costs before it starts: per case, the recorded text (history, turn context, input, tool
/// results, reply) as tokens at about four characters each, sent once per model call (tool calls plus one), priced
/// with the model the candidate uses for the case's tier.
/// </summary>
public sealed class EvalCostEstimator(IModelRouter router, ICostCalculator costs)
{
    private const int CharactersPerToken = 4;

    /// <summary>A fixed allowance for the system prompt and tool definitions per model call.</summary>
    private const int PromptTokens = 2000;

    public decimal Estimate(IEnumerable<EvalCase> cases, CandidateConfig candidate) => cases.Sum(evalCase => EstimateCase(evalCase, candidate));

    private decimal EstimateCase(EvalCase evalCase, CandidateConfig candidate)
    {
        ModelTier tier = evalCase.InputMode == InputMode.Voice ? ModelTier.Fast : ModelTier.Standard;
        string? key = candidate.Models.TryGetValue(tier, out string? chosen) ? chosen : router.Route(new ModelRequest(tier, Tools: true)) is { IsSuccess: true } routed ? routed.Value.Key : null;
        if (key is null)
            return 0;

        long text = evalCase.History.Sum(message => message.Text.Length) + (evalCase.TurnContext?.Length ?? 0) + evalCase.Input.Length
            + evalCase.ToolCalls.Sum(call => (call.Result?.Length ?? 0) + call.Arguments.Length);
        int calls = evalCase.ToolCalls.Count + 1;
        long input = calls * (PromptTokens + text / CharactersPerToken);
        long output = calls * Math.Max(50, (evalCase.Reply?.Length ?? 0) / CharactersPerToken);
        return costs.Calculate(key, new UsageDetails { InputTokenCount = input, OutputTokenCount = output, TotalTokenCount = input + output });
    }
}
