using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;

namespace Marshal.Core.Extraction;

/// <summary>
/// <c>agent.extract</c>: asks a typed question about an untrusted item by reference (<c>mail:…</c>, <c>rss:…</c>,
/// <c>note:…</c>). The content is resolved server-side and given only to the quarantined extractor; the model gets the
/// answer alone. Typed answers leave the run untainted; a <c>text</c> answer taints it with the item's source and is
/// wrapped as untrusted data. Hidden while no content source is available.
/// </summary>
public sealed class ExtractTool(IEnumerable<IUntrustedContentResolver> resolvers, IQuarantinedExtractor extractor) : ITool
{
    private static readonly Dictionary<string, AnswerType> _types = new(StringComparer.Ordinal)
    {
        ["boolean"] = AnswerType.Boolean,
        ["number"] = AnswerType.Number,
        ["integer"] = AnswerType.Integer,
        ["date"] = AnswerType.Date,
        ["datetime"] = AnswerType.DateTime,
        ["enum"] = AnswerType.Enum,
        ["text"] = AnswerType.Text,
    };

    private readonly IReadOnlyList<IUntrustedContentResolver> _resolvers = [.. resolvers];

    public ToolDescriptor Descriptor { get; } = new()
    {
        Id = new ToolId("agent", "extract"),
        Description = "Answers a question about a mail, news article or note without reading it yourself. Pass the item's reference "
            + "(mail:<message id>, rss:<article id>, note:<path>) and ask for a typed answer. Prefer typed answers (boolean, number, integer, "
            + "date, datetime, enum): they keep the conversation trusted. A text answer is untrusted content. The answer may be \"unknown\".",
        ParametersSchema = JsonDocument.Parse("""
            {"type":"object","properties":{
              "ref":{"type":"string","description":"Item reference, e.g. mail:private:4711, rss:12345, note:projects/voron.md."},
              "question":{"type":"string","description":"One question about the item."},
              "answerType":{"type":"string","enum":["boolean","number","integer","date","datetime","enum","text"]},
              "values":{"type":"array","items":{"type":"string"},"description":"For enum: the allowed answers."}},
              "required":["ref","question","answerType"],"additionalProperties":false}
            """).RootElement.Clone(),
    };

    public async ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken)
    {
        foreach (IUntrustedContentResolver resolver in _resolvers)
        {
            if (await resolver.IsAvailableAsync(cancellationToken))
                return true;
        }

        return false;
    }

    public Result Validate(JsonElement arguments)
    {
        if (Text(arguments, "ref") is null || Text(arguments, "question") is null)
            return Invalid("ref and question are required.");
        if (Text(arguments, "answerType") is not { } type || !_types.TryGetValue(type, out AnswerType answerType))
            return Invalid($"answerType must be one of {string.Join(", ", _types.Keys)}.");
        if (answerType == AnswerType.Enum && Values(arguments) is not { Count: > 0 })
            return Invalid("An enum answer needs values.");

        return Result.Success();
    }

    public string Summarize(JsonElement arguments, string language) => $"{Text(arguments, "ref")}: {Text(arguments, "question")}";

    public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
    {
        JsonElement arguments = invocation.Arguments;
        Result valid = Validate(arguments);
        if (valid.IsFailure)
            return valid.Error;

        string reference = Text(arguments, "ref")!;
        int colon = reference.IndexOf(':', StringComparison.Ordinal);
        IUntrustedContentResolver? resolver = colon <= 0 ? null : _resolvers.FirstOrDefault(candidate => candidate.Kind == reference[..colon]);
        if (resolver is null || colon == reference.Length - 1)
            return new Error(ExtractionAnswer.NotFoundCode, $"Nothing can be read from '{reference}'. Use mail:<message id>, rss:<article id> or note:<path>.", ErrorType.NotFound);

        Result<UntrustedItem> item = await resolver.ResolveAsync(reference[(colon + 1)..], cancellationToken);
        if (item.IsFailure)
            return item.Error;

        var spec = new AnswerSpec(_types[Text(arguments, "answerType")!], Values(arguments));
        Result<ExtractionAnswer> answer = await extractor.ExtractAsync(item.Value, Text(arguments, "question")!, spec, invocation.Context, cancellationToken);
        if (answer.IsFailure)
            return answer.Error;

        string content = JsonSerializer.Serialize(new { answer = answer.Value.Value });
        return new ToolOutput(content, item.Value.Reference) { TaintKind = answer.Value.Taints ? item.Value.TaintKind : null };
    }

    private static IReadOnlyList<string>? Values(JsonElement arguments) =>
        arguments.TryGetProperty("values", out JsonElement values) && values.ValueKind == JsonValueKind.Array
            ? [.. values.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).Where(value => value.Length > 0)]
            : null;

    private static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text.Trim() : null;

    private static Error Invalid(string message) => new("tool.invalid_arguments", message, ErrorType.Validation);
}
