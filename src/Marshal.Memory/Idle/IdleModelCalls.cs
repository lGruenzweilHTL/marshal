using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Microsoft.Extensions.AI;

namespace Marshal.Memory.Idle;

/// <summary>
/// One <c>fast</c> tier model call with a JSON Schema answer for an idle-time job, recorded as a task run (trigger
/// <c>memory_idle:&lt;job&gt;</c>), so its cost counts against the global daily cost cap. Nothing is called when the
/// cap is used up.
/// </summary>
public sealed class IdleModelCalls(
    IModelRouter router,
    IRunRecorder recorder,
    ICostCalculator costs,
    IDailyCostCap cap,
    IExecutionContextAccessor executionContext,
    TimeProvider timeProvider)
{
    public const string TriggerPrefix = "memory_idle:";

    /// <summary>The owner the job runs for.</summary>
    public OwnerId Owner => executionContext.Current?.OwnerId ?? throw new InvalidOperationException("Memory accessed without an execution context.");

    /// <summary>The answer as a JSON object, or an error (cap reached, no model, invalid output).</summary>
    public async Task<Result<JsonElement>> AskAsync(string job, string instructions, string input, JsonElement schema, CancellationToken cancellationToken)
    {
        MarshalExecutionContext current = executionContext.Current ?? throw new InvalidOperationException("Memory accessed without an execution context.");
        if (await cap.LeftTodayAsync(current.OwnerId, cancellationToken) <= 0)
            return new Error("memory.cost_cap_reached", "The daily cost cap is used up; idle-time memory work waits for tomorrow.", ErrorType.Unavailable);

        Result<RoutedModel> routed = router.Route(new ModelRequest(ModelTier.Fast));
        if (routed.IsFailure)
            return routed.Error;

        MarshalExecutionContext context = new() { OwnerId = current.OwnerId, Source = SessionSource.Task, IsInteractive = false };
        using IDisposable scope = executionContext.Use(context);
        RunId run = await recorder.StartRunAsync(new RunStart(context, TriggerPrefix + job), cancellationToken);
        await recorder.RecordStepAsync(run, new RunStepRecord { Kind = RunStepKind.Context, Content = instructions, Input = input }, cancellationToken);

        RoutedModel model = routed.Value;
        long started = timeProvider.GetTimestamp();
        ChatResponse response = await model.Client.GetResponseAsync(
            [new ChatMessage(ChatRole.System, instructions), new ChatMessage(ChatRole.User, input)],
            new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(schema, job) },
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
            return new Error("memory.idle_invalid_output", $"The {job} model did not answer with the requested JSON.", ErrorType.Failure);
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
