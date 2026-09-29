using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Rss;
using Marshal.Abstractions.Tools;

namespace Marshal.Integrations.FreshRss.Tools;

/// <summary>
/// Model-facing RSS tools. Article content comes from feeds and is untrusted (taint source <c>rss</c>).
/// <c>rss.fetch_full_text</c> takes only an article id and reads the link from the feed reader, so the model can
/// never make Marshal fetch a URL of its choosing.
/// </summary>
internal static class RssTools
{
    private const string Taint = "rss";
    private const int MaxLimit = 50;

    private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    public static IReadOnlyList<ITool> Create(IRssClient client, IFullTextExtractor extractor)
    {
        var context = new RssToolContext(client, extractor);
        return
        [
            new RssTool(context, "list_feeds", "Lists the subscribed feeds with their categories.", """
                {"type":"object","properties":{}}
                """, untrusted: false, ListFeedsAsync),
            new RssTool(context, "list_categories", "Lists the feed categories.", """
                {"type":"object","properties":{}}
                """, untrusted: false, ListCategoriesAsync),
            new RssTool(context, "get_unread", "Lists unread articles, newest first, with a short summary.", """
                {"type":"object","properties":{
                  "category":{"type":"string","description":"Category name from rss_list_categories; all when omitted."},
                  "since":{"type":"string","description":"Only articles fetched after this date or time (YYYY-MM-DD or ISO 8601)."},
                  "limit":{"type":"integer","minimum":1,"maximum":50}}}
                """, untrusted: true, GetUnreadAsync),
            new RssTool(context, "get_article", "Reads one article's feed content.", """
                {"type":"object","properties":{"id":{"type":"string","description":"Article id from rss_get_unread or rss_search."}},"required":["id"]}
                """, untrusted: true, GetArticleAsync),
            new RssTool(context, "search", "Searches recent articles (read and unread) by words in title or summary.", """
                {"type":"object","properties":{
                  "query":{"type":"string"},
                  "limit":{"type":"integer","minimum":1,"maximum":50}},"required":["query"]}
                """, untrusted: true, SearchAsync),
            new RssTool(context, "mark_read", "Marks articles as read.", """
                {"type":"object","properties":{"ids":{"type":"array","items":{"type":"string"},"description":"Article ids."}},"required":["ids"]}
                """, untrusted: false, MarkReadAsync),
            new RssTool(context, "fetch_full_text", "Fetches the full text of an article from its web page. Takes an article id, never a URL.", """
                {"type":"object","properties":{"articleId":{"type":"string","description":"Article id from rss_get_unread or rss_search."}},"required":["articleId"],"additionalProperties":false}
                """, untrusted: true, FetchFullTextAsync, ValidateFullText),
        ];
    }

    private static async Task<Result<ToolOutput>> ListFeedsAsync(RssToolContext context, JsonElement arguments, CancellationToken ct) =>
        (await context.Client.ListFeedsAsync(ct)).Map(feeds => new ToolOutput(JsonSerializer.Serialize(feeds.Select(feed => new { feed.Title, feed.Categories, feed.SiteUrl }), _json)));

    private static async Task<Result<ToolOutput>> ListCategoriesAsync(RssToolContext context, JsonElement arguments, CancellationToken ct) =>
        (await context.Client.ListCategoriesAsync(ct)).Map(categories => new ToolOutput(JsonSerializer.Serialize(categories.Select(category => category.Label), _json)));

    private static async Task<Result<ToolOutput>> GetUnreadAsync(RssToolContext context, JsonElement arguments, CancellationToken ct)
    {
        DateTimeOffset? since = null;
        if (Text(arguments, "since") is { } text)
        {
            if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out DateTimeOffset parsed))
                return Invalid("since must be a date like 2026-09-29 or an ISO 8601 time.");
            since = parsed;
        }

        Result<IReadOnlyList<RssArticle>> articles = await context.Client.GetUnreadAsync(Text(arguments, "category"), since, Limit(arguments), ct);
        return articles.Map(Summaries);
    }

    private static async Task<Result<ToolOutput>> GetArticleAsync(RssToolContext context, JsonElement arguments, CancellationToken ct)
    {
        if (Text(arguments, "id") is not { } id)
            return Invalid("id is required.");

        Result<RssArticle> article = await context.Client.GetArticleAsync(id, ct);
        return article.Map(found => new ToolOutput(JsonSerializer.Serialize(new
        {
            found.Id,
            found.Title,
            feed = found.FeedTitle,
            found.Published,
            found.Unread,
            found.Categories,
            found.Summary,
        }, _json), found.Id));
    }

    private static async Task<Result<ToolOutput>> SearchAsync(RssToolContext context, JsonElement arguments, CancellationToken ct)
    {
        if (Text(arguments, "query") is not { } query)
            return Invalid("query is required.");

        return (await context.Client.SearchAsync(query, Limit(arguments), ct)).Map(Summaries);
    }

    private static async Task<Result<ToolOutput>> MarkReadAsync(RssToolContext context, JsonElement arguments, CancellationToken ct)
    {
        string[] ids = arguments.TryGetProperty("ids", out JsonElement list) && list.ValueKind == JsonValueKind.Array
            ? [.. list.EnumerateArray().Where(item => item.ValueKind == JsonValueKind.String).Select(item => item.GetString()!.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal)]
            : [];
        if (ids.Length == 0)
            return Invalid("ids must list at least one article id.");

        Result marked = await context.Client.MarkReadAsync(ids, ct);
        return marked.IsSuccess
            ? new ToolOutput(string.Create(CultureInfo.InvariantCulture, $"Marked {ids.Length} article(s) as read."))
            : marked.Error;
    }

    private static async Task<Result<ToolOutput>> FetchFullTextAsync(RssToolContext context, JsonElement arguments, CancellationToken ct)
    {
        Result valid = ValidateFullText(arguments);
        if (valid.IsFailure)
            return valid.Error;

        Result<RssArticle> article = await context.Client.GetArticleAsync(Text(arguments, "articleId")!, ct);
        if (article.IsFailure)
            return article.Error;
        if (article.Value.Link is not { } link)
            return RssErrors.FullTextFailed("the article has no link");

        Result<FullText> text = await context.Extractor.ExtractAsync(link, ct);
        return text.Map(full => new ToolOutput(
            $"Title: {full.Title}\nArticle: {article.Value.Id} ({article.Value.FeedTitle})\n\n{full.Text}" + (full.Truncated ? "\n\n[Text cut at the length limit.]" : ""),
            article.Value.Id));
    }

    // Only an article id: a URL (or any other argument) never reaches an HTTP request.
    private static Result ValidateFullText(JsonElement arguments)
    {
        if (arguments.ValueKind != JsonValueKind.Object || arguments.EnumerateObject().Any(property => property.Name != "articleId"))
            return Invalid("fetch_full_text takes only articleId.");

        return Text(arguments, "articleId") is { } id && FreshRssClient.IsArticleId(id)
            ? Result.Success()
            : Invalid("articleId must be an article id from rss_get_unread or rss_search, not a URL.");
    }

    private static ToolOutput Summaries(IReadOnlyList<RssArticle> articles) => new(
        JsonSerializer.Serialize(articles.Select(article => new
        {
            article.Id,
            article.Title,
            feed = article.FeedTitle,
            article.Published,
            article.Unread,
            summary = article.Summary.Length > 300 ? article.Summary[..300] + "…" : article.Summary,
        }), _json),
        string.Join(',', articles.Select(article => article.Id)));

    private static int Limit(JsonElement arguments) =>
        Math.Clamp(arguments.TryGetProperty("limit", out JsonElement limit) && limit.TryGetInt32(out int value) ? value : 20, 1, MaxLimit);

    private static string? Text(JsonElement arguments, string name) =>
        arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text.Trim() : null;

    private static Error Invalid(string message) => new("rss.invalid_arguments", message, ErrorType.Validation);

    private sealed record RssToolContext(IRssClient Client, IFullTextExtractor Extractor);

    private sealed class RssTool(
        RssToolContext context,
        string name,
        string description,
        string schema,
        bool untrusted,
        Func<RssToolContext, JsonElement, CancellationToken, Task<Result<ToolOutput>>> run,
        Func<JsonElement, Result>? validate = null) : ITool
    {
        public ToolDescriptor Descriptor { get; } = new()
        {
            Id = new ToolId("rss", name),
            Description = description,
            ParametersSchema = JsonDocument.Parse(schema).RootElement.Clone(),
            DefaultTier = RiskTier.Safe,
            UntrustedOutput = untrusted,
            TaintKind = untrusted ? Taint : null,
        };

        public ValueTask<bool> IsAvailableAsync(MarshalExecutionContext executionContext, CancellationToken cancellationToken) =>
            ValueTask.FromResult(context.Client.IsConfigured);

        public Result Validate(JsonElement arguments) => validate?.Invoke(arguments) ?? Result.Success();

        public Task<Result<ToolOutput>> InvokeAsync(ToolInvocation invocation, CancellationToken cancellationToken) =>
            run(context, invocation.Arguments.ValueKind == JsonValueKind.Object ? invocation.Arguments : JsonDocument.Parse("{}").RootElement, cancellationToken);
    }
}
