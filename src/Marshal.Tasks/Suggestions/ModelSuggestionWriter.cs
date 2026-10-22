using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Models;

namespace Marshal.Tasks.Suggestions;

/// <summary>Words a suggestion from a detector's facts.</summary>
public interface ISuggestionWriter
{
    /// <summary>At most two sentences in <paramref name="language"/>, or an error (cap reached, no model, invalid output).</summary>
    Task<Result<string>> WriteAsync(string detector, IReadOnlyDictionary<string, string> facts, string language, CancellationToken cancellationToken);
}

/// <summary>
/// One <c>fast</c> tier call without tools and with a JSON Schema answer <c>{ text }</c>, recorded as a run with trigger
/// <c>suggestion:&lt;detector&gt;</c> so its cost counts against the daily cost cap. The facts are the owner's own
/// device data, so the call runs untainted.
/// </summary>
public sealed class ModelSuggestionWriter(BackgroundModelCalls calls) : ISuggestionWriter
{
    public const string TriggerPrefix = "suggestion:";

    private const int MaxLength = 400;

    private static readonly JsonElement _schema = BackgroundModelCalls.Schema("""
        {"type":"object","properties":{"text":{"type":"string"}},"required":["text"],"additionalProperties":false}
        """);

    public async Task<Result<string>> WriteAsync(string detector, IReadOnlyDictionary<string, string> facts, string language, CancellationToken cancellationToken)
    {
        Result<JsonElement> answer = await calls.AskAsync(ModelTier.Fast, TriggerPrefix + detector, Instructions(language), Input(detector, facts), _schema, "suggestion", cancellationToken);
        if (answer.IsFailure)
            return answer.Error;

        if (!answer.Value.TryGetProperty("text", out JsonElement text) || text.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(text.GetString()))
            return new Error("suggestion.invalid_output", "The suggestion model answered without text.", ErrorType.Failure);

        string worded = text.GetString()!.Trim();
        return worded.Length > MaxLength ? worded[..MaxLength].TrimEnd() + "…" : worded;
    }

    private static string Instructions(string language)
    {
        string name = language.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? "German" : "English";
        return $"""
            You word a short suggestion from a home assistant to its user. Write in {name}, at most two sentences,
            addressing the user directly. Use only the facts given; do not add numbers, devices or advice that are not in
            them. Answer with JSON: {"{"}"text": "..."{"}"}.
            """;
    }

    private static string Input(string detector, IReadOnlyDictionary<string, string> facts)
    {
        var input = new StringBuilder($"Kind: {detector}\nFacts:\n");
        foreach ((string key, string value) in facts.OrderBy(fact => fact.Key, StringComparer.Ordinal))
            input.Append("- ").Append(key).Append(": ").Append(value).Append('\n');
        return input.ToString();
    }
}
