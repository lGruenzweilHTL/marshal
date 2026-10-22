using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Microsoft.Extensions.AI;

namespace Marshal.Tasks.Suggestions;

/// <summary>
/// One tool-less model call with a JSON Schema answer for background learning work (suggestion wording, skill
/// drafts), recorded as a run with the given trigger so its cost counts against the daily cost cap. Nothing is called
/// when the cap is used up.
/// </summary>
public sealed class BackgroundModelCalls(
    IModelRouter router,
    IRunRecorder recorder,
    ICostCalculator costs,
    IDailyCostCap cap,
    IExecutionContextAccessor executionContext,
    TimeProvider timeProvider)
{
    /// <summary>The answer as a JSON object, or an error (cap reached, no model, invalid output).</summary>
    public async Task<Result<JsonElement>> AskAsync(
        ModelTier tier, string trigger, string instructions, string input, JsonElement schema, string schemaName, CancellationToken cancellationToken)
    {
        MarshalExecutionContext current = executionContext.Current ?? throw new InvalidOperationException("Model called without an execution context.");
        if (await cap.LeftTodayAsync(current.OwnerId, cancellationToken) <= 0)
            return new Error("learning.cost_cap_reached", "The daily cost cap is used up; this waits for tomorrow.", ErrorType.Unavailable);

        Result<RoutedModel> routed = router.Route(new ModelRequest(tier));
        if (routed.IsFailure)
            return routed.Error;

        MarshalExecutionContext context = new() { OwnerId = current.OwnerId, Source = SessionSource.Task, IsInteractive = false, Language = current.Language };
        using IDisposable scope = executionContext.Use(context);
        RunId run = await recorder.StartRunAsync(new RunStart(context, trigger), cancellationToken);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Context, Content = instructions, Input = input }, cancellationToken);

        RoutedModel model = routed.Value;
        long started = timeProvider.GetTimestamp();
        ChatResponse response = await model.Client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, input)],
            new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, schemaName) },
            cancellationToken);
        await recorder.RecordStepAsync(run, new RunStepRecord
        {
            Kind = RunStepKind.ModelCall,
            Model = model.Key,
            Tier = model.Tier.ToString().ToLowerInvariant(),
            TokensIn = response.Usage?.InputTokenCount,
            TokensOut = response.Usage?.OutputTokenCount,
            TokensCached = response.Usage?.CachedInputTokenCount,
            Content = response.Text,
            DurationMs = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds,
            Cost = costs.Calculate(model.Key, response.Usage),
        }, cancellationToken);

        if (Parse(response.Text) is not { } answer)
        {
            await recorder.CompleteRunAsync(run, new RunCompletion(RunOutcome.Error, "invalid_output", []), cancellationToken);
            return new Error("learning.invalid_output", "The model did not answer with the requested JSON.", ErrorType.Failure);
        }

        await recorder.CompleteRunAsync(run, new RunCompletion(RunOutcome.Ok, null, []), cancellationToken);
        return answer;
    }

    /// <summary>A JSON Schema from its text.</summary>
    public static JsonElement Schema(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static JsonElement? Parse(string output)
    {
        string json = output.Trim();
        int firstLine = json.IndexOf('\n', StringComparison.Ordinal);
        int fenceEnd = json.LastIndexOf("```", StringComparison.Ordinal);
        if (json.StartsWith("```", StringComparison.Ordinal) && firstLine > 0 && fenceEnd > firstLine)
            json = json[(firstLine + 1)..fenceEnd];

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.ValueKind == JsonValueKind.Object ? document.RootElement.Clone() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
