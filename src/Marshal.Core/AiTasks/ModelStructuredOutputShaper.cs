using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Runs;
using Microsoft.Extensions.AI;

namespace Marshal.Core.AiTasks;

/// <summary>
/// <see cref="IStructuredOutputShaper"/> with the run's model: one call with the schema as response format and no
/// tools, validated with <see cref="JsonSchemaSubsetValidator"/>; on failure one repair call that lists the errors.
/// </summary>
public sealed class ModelStructuredOutputShaper(IRunRecorder recorder, ICostCalculator costs, TimeProvider timeProvider) : IStructuredOutputShaper
{
    private const int Attempts = 2;

    private const string Prompt =
        "Convert the answer below into JSON that matches the given JSON Schema. Use only facts from the answer. " +
        "Reply with the JSON object only.";

    public async Task<Result<JsonElement>> ShapeAsync(StructuredOutputRequest request, MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        var options = new ChatOptions { ResponseFormat = ChatResponseFormat.ForJsonSchema(request.Schema, "result") };
        List<ChatMessage> messages =
        [
            new(ChatRole.System, Prompt),
            new(ChatRole.User, $"Instructions:\n{request.Instructions}\n\nAnswer:\n{request.Reply}\n\nJSON Schema:\n{request.Schema.GetRawText()}"),
        ];

        IReadOnlyList<string> errors = [];
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            ChatResponse response = await CallAsync(request.Model, messages, options, context, cancellationToken);
            Result<JsonElement> checkedOutput = Check(response.Text, request.Schema);
            if (checkedOutput.IsSuccess)
                return checkedOutput;

            errors = [checkedOutput.Error.Message];
            messages.AddRange(response.Messages);
            messages.Add(new ChatMessage(ChatRole.User, $"That output is invalid:\n{checkedOutput.Error.Message}\nReply with corrected JSON only."));
        }

        return new Error(StructuredOutput.InvalidOutputCode, $"The model output does not match the structure: {string.Join(" ", errors)}", ErrorType.Failure);
    }

    private static Result<JsonElement> Check(string text, JsonElement schema)
    {
        JsonElement output;
        try
        {
            using var document = JsonDocument.Parse(Unfence(text));
            output = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            return new Error(StructuredOutput.InvalidOutputCode, $"The output is not valid JSON: {exception.Message}", ErrorType.Validation);
        }

        IReadOnlyList<string> errors = JsonSchemaSubsetValidator.Validate(schema, output);
        return errors.Count == 0 ? output : new Error(StructuredOutput.InvalidOutputCode, string.Join("\n", errors), ErrorType.Validation);
    }

    // Some models wrap JSON in a Markdown code fence despite the response format.
    private static string Unfence(string text)
    {
        string trimmed = text.Trim();
        if (!trimmed.StartsWith("```", StringComparison.Ordinal) || !trimmed.EndsWith("```", StringComparison.Ordinal) || trimmed.Length < 6)
            return trimmed;

        int firstLine = trimmed.IndexOf('\n', StringComparison.Ordinal);
        return firstLine < 0 ? trimmed : trimmed[(firstLine + 1)..^3].Trim();
    }

    private async Task<ChatResponse> CallAsync(RoutedModel model, List<ChatMessage> messages, ChatOptions options, MarshalExecutionContext context, CancellationToken ct)
    {
        long started = timeProvider.GetTimestamp();
        ChatResponse response = await model.Client.GetResponseAsync(messages, options, ct);
        decimal cost = costs.Calculate(model.Key, response.Usage);
        context.Usage.AddModelCall(cost, (response.Usage?.InputTokenCount ?? 0) + (response.Usage?.OutputTokenCount ?? 0));
        if (context.RunId is { } runId)
        {
            await recorder.RecordStepAsync(runId, new RunStepRecord
            {
                Kind = RunStepKind.ModelCall,
                Model = model.Key,
                Tier = model.Tier.ToString().ToLowerInvariant(),
                TokensIn = response.Usage?.InputTokenCount,
                TokensOut = response.Usage?.OutputTokenCount,
                TokensCached = response.Usage?.CachedInputTokenCount,
                DurationMs = (int)timeProvider.GetElapsedTime(started).TotalMilliseconds,
                Cost = cost,
                Content = response.Text,
            }, ct);
        }

        return response;
    }
}
