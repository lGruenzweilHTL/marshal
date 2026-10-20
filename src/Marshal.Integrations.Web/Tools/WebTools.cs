using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Tools;
using Marshal.Abstractions.Web;

namespace Marshal.Integrations.Web.Tools;

/// <summary>
/// Model-facing web search tools. Results are written by strangers: <c>web.search</c> output is untrusted (taint source
/// <c>web</c>); <c>web.answer</c> gives the snippets only to the quarantined extractor, so typed answers leave the run
/// untainted. Queries are safe in every run: they reach the configured engines, not a host an attacker picks.
/// </summary>
internal static class WebTools
{
    public const string Taint = "web";
    public const int MaxQueryLength = 300;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public static IReadOnlyList<ITool> Create(IWebSearchProvider provider, ISessionWebResults results, IQuarantinedExtractor extractor) =>
    [
        new WebTool(provider, new ToolDescriptor
        {
            Id = new ToolId("web", "search"),
            Description = "Searches the web. Returns title, snippet, URL, domain and a result id per hit; result pages are never opened. "
                + "Results are untrusted content. For one fact, prefer web_answer, or agent_extract with ref web:<result id>.",
            ParametersSchema = JsonDocument.Parse("""
                {"type":"object","properties":{
                  "query":{"type":"string","description":"Search query, at most 300 characters."},
                  "limit":{"type":"integer","minimum":1,"maximum":20}},"required":["query"],"additionalProperties":false}
                """).RootElement.Clone(),
            UntrustedOutput = true,
            TaintKind = Taint,
            Fillers = ToolFillers.Lines("Ich suche im Web.", "Let me search the web."),
        }, ValidateSearch, (invocation, ct) => SearchAsync(provider, results, invocation, ct)),
        new WebTool(provider, new ToolDescriptor
        {
            Id = new ToolId("web", "answer"),
            Description = "Answers one question from web search snippets with a typed answer, without you reading the results. "
                + "Prefer typed answers (boolean, number, integer, date, datetime, enum): they keep the conversation trusted. A text answer is "
                + "untrusted content. Returns the answer (may be \"unknown\") and the domains it came from.",
            ParametersSchema = JsonDocument.Parse("""
                {"type":"object","properties":{
                  "question":{"type":"string","description":"The question, used as the search query."},
                  "answerType":{"type":"string","enum":["boolean","number","integer","date","datetime","enum","text"]},
                  "values":{"type":"array","items":{"type":"string"},"description":"For enum: the allowed answers."}},
                  "required":["question","answerType"],"additionalProperties":false}
                """).RootElement.Clone(),
            Fillers = ToolFillers.Lines("Ich sehe im Web nach.", "Let me look that up."),
        }, ValidateAnswer, (invocation, ct) => AnswerAsync(provider, extractor, invocation, ct)),
    ];

    /// <summary>The text the extractor sees for one result: title and snippet, never the page.</summary>
    public static string Describe(WebResult result) => $"Title: {result.Title}\nDomain: {result.Domain}\nSnippet: {result.Snippet}";

    private static async Task<Result<ToolOutput>> SearchAsync(IWebSearchProvider provider, ISessionWebResults results, ToolInvocation invocation, CancellationToken ct)
    {
        string query = Query(invocation.Arguments, "query")!;
        int limit = Math.Clamp(invocation.Arguments.TryGetProperty("limit", out JsonElement value) && value.TryGetInt32(out int requested) ? requested : provider.MaxResults, 1, provider.MaxResults);
        Result<IReadOnlyList<WebResult>> found = await provider.SearchAsync(query, invocation.Context.Language, limit, ct);
        if (found.IsFailure)
            return found.Error;

        var hits = found.Value.Select(result => new { id = results.Add(invocation.Context, result), result.Title, result.Snippet, url = result.Url.ToString(), result.Domain }).ToList();
        return new ToolOutput(JsonSerializer.Serialize(hits, _json), string.Join(',', hits.Select(hit => $"web:{hit.id}")));
    }

    private static async Task<Result<ToolOutput>> AnswerAsync(IWebSearchProvider provider, IQuarantinedExtractor extractor, ToolInvocation invocation, CancellationToken ct)
    {
        string question = Query(invocation.Arguments, "question")!;
        Result<IReadOnlyList<WebResult>> found = await provider.SearchAsync(question, invocation.Context.Language, provider.MaxResults, ct);
        if (found.IsFailure)
            return found.Error;

        IReadOnlyList<string> domains = [.. found.Value.Select(result => result.Domain).Distinct(StringComparer.OrdinalIgnoreCase)];
        if (found.Value.Count == 0)
            return new ToolOutput(JsonSerializer.Serialize(new { answer = "unknown", domains }, _json));

        var content = new StringBuilder();
        for (int index = 0; index < found.Value.Count; index++)
            content.Append(System.Globalization.CultureInfo.InvariantCulture, $"[{index + 1}] {Describe(found.Value[index])}\n\n");

        var item = new UntrustedItem("web:search", content.ToString().TrimEnd(), Taint);
        Result<ExtractionAnswer> answer = await extractor.ExtractAsync(item, question, Spec(invocation.Arguments)!, invocation.Context, ct);
        if (answer.IsFailure)
            return answer.Error;

        return new ToolOutput(JsonSerializer.Serialize(new { answer = answer.Value.Value, domains }, _json)) { TaintKind = answer.Value.Taints ? Taint : null };
    }

    private static Result ValidateSearch(JsonElement arguments) =>
        Query(arguments, "query") is null ? Invalid($"query is required and may have at most {MaxQueryLength} characters.") : Result.Success();

    private static Result ValidateAnswer(JsonElement arguments)
    {
        if (Query(arguments, "question") is null)
            return Invalid($"question is required and may have at most {MaxQueryLength} characters.");
        if (Spec(arguments) is not { } spec)
            return Invalid("answerType must be one of boolean, number, integer, date, datetime, enum, text.");
        if (spec.Type == AnswerType.Enum && spec.Values is not { Count: > 0 })
            return Invalid("An enum answer needs values.");

        return Result.Success();
    }

    private static AnswerSpec? Spec(JsonElement arguments)
    {
        if (Text(arguments, "answerType") is not { } name || !name.All(char.IsAsciiLetterLower) || !Enum.TryParse(name, ignoreCase: true, out AnswerType type))
            return null;

        IReadOnlyList<string>? values = arguments.TryGetProperty("values", out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(value => value.ValueKind == JsonValueKind.String).Select(value => value.GetString()!).Where(value => value.Length > 0)]
            : null;
        return new AnswerSpec(type, values);
    }

    private static string? Query(JsonElement arguments, string name) => Text(arguments, name) is { Length: <= MaxQueryLength } text ? text : null;

    private static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text.Trim() : null;

    private static Error Invalid(string message) => new("tool.invalid_arguments", message, ErrorType.Validation);

    private sealed class WebTool(
        IWebSearchProvider provider,
        ToolDescriptor descriptor,
        Func<JsonElement, Result> validate,
        Func<ToolInvocation, CancellationToken, Task<Result<ToolOutput>>> run) : ITool
    {
        public ToolDescriptor Descriptor => descriptor;

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext context, CancellationToken cancellationToken) => ValueTask.FromResult(provider.IsConfigured);

        public Result Validate(JsonElement arguments) => validate(arguments);

        public string Summarize(JsonElement arguments, string language) => Text(arguments, "query") ?? Text(arguments, "question") ?? "";

        public async Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken)
        {
            Result valid = validate(invocation.Arguments);
            return valid.IsFailure ? valid.Error : await run(invocation, cancellationToken);
        }
    }
}
