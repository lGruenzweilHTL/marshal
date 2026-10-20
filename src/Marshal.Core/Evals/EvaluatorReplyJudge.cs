using Marshal.Abstractions;
using Marshal.Abstractions.Evals;
using Marshal.Abstractions.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Quality;

namespace Marshal.Core.Evals;

/// <summary>
/// Scores a replayed reply with LLM-judged evaluators from <c>Microsoft.Extensions.AI.Evaluation.Quality</c>: equivalence
/// with the expected answer (the recorded reply of a good case, the note of a bad one) and relevance to the input.
/// Each 1–5 metric becomes 0–1; the score is their mean. Judge calls are priced like any model call.
/// </summary>
public sealed class EvaluatorReplyJudge(EvalModels models, ICostCalculator costs, IEnumerable<IEvaluator>? evaluators = null) : IReplyJudge
{
    private readonly IEvaluator[] _evaluators = [.. evaluators ?? [new EquivalenceEvaluator(), new RelevanceEvaluator()]];

    public async Task<JudgeScore> ScoreAsync(EvalCase evalCase, string reply, string judgeModel, CancellationToken cancellationToken)
    {
        Result<RoutedModel> model = models.Get(judgeModel);
        if (model.IsFailure)
            return new JudgeScore(null, 0);

        var counting = new UsageCountingClient(model.Value.Client);
        var configuration = new ChatConfiguration(counting);
        ChatMessage[] messages = [new(ChatRole.User, evalCase.Input)];
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, reply));
        EvaluationContext[] context = [new EquivalenceEvaluatorContext(Expected(evalCase))];

        var scores = new List<double>();
        foreach (IEvaluator evaluator in _evaluators)
        {
            EvaluationResult result = await evaluator.EvaluateAsync(messages, response, configuration, context, cancellationToken);
            scores.AddRange(result.Metrics.Values.OfType<NumericMetric>().Where(metric => metric.Value is not null).Select(metric => Math.Clamp((metric.Value!.Value - 1) / 4, 0, 1)));
        }

        return new JudgeScore(scores.Count == 0 ? null : scores.Average(), costs.Calculate(judgeModel, counting.Usage));
    }

    // A bad case's recorded reply is what not to say; its note says what was expected instead.
    private static string Expected(EvalCase evalCase) => evalCase.Expectation == EvalExpectation.Bad
        ? $"The expected behavior: {evalCase.Note ?? "anything but the recorded reply"}. The recorded reply, rated wrong, was: {evalCase.Reply}"
        : evalCase.Reply ?? "";

    private sealed class UsageCountingClient(IChatClient inner) : DelegatingChatClient(inner)
    {
        private readonly Lock _lock = new();

        public UsageDetails Usage { get; } = new() { InputTokenCount = 0, OutputTokenCount = 0, TotalTokenCount = 0 };

        public override async Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            ChatResponse response = await base.GetResponseAsync(messages, options, cancellationToken);
            if (response.Usage is { } usage)
            {
                lock (_lock)
                    Usage.Add(usage);
            }

            return response;
        }
    }
}
