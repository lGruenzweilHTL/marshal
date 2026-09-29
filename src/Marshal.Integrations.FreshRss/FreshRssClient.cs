using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Rss;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.FreshRss;

/// <summary>
/// FreshRSS through its Google Reader-compatible API: <c>ClientLogin</c> with the API password, the auth token kept
/// and renewed once when a request is rejected, edit tokens fetched per edit. Article ids are the decimal form of
/// the Google Reader item id.
/// </summary>
public sealed class FreshRssClient(IHttpClientFactory httpClients, IOptions<FreshRssOptions> options) : IRssClient, IDisposable
{
    public const string HttpClientName = "freshrss";

    private const string ItemPrefix = "tag:google.com,2005:reader/item/";
    private const string ReadState = "user/-/state/com.google/read";
    private const string LabelPrefix = "user/-/label/";

    // Internal marker for a rejected auth token; callers see rss.auth_failed after the renewal also failed.
    private static readonly Error Unauthorized = new("rss.unauthorized", "Token rejected.", ErrorType.Unavailable);

    private readonly FreshRssOptions _options = options.Value;
    private readonly SemaphoreSlim _login = new(1, 1);
    private string? _auth;

    public bool IsConfigured => _options.IsConfigured;

    public void Dispose() => _login.Dispose();

    public async Task<Result<IReadOnlyList<RssFeed>>> ListFeedsAsync(CancellationToken cancellationToken)
    {
        Result<JsonElement> json = await GetJsonAsync("reader/api/0/subscription/list?output=json", cancellationToken);
        return json.Map<IReadOnlyList<RssFeed>>(root => [.. root.GetProperty("subscriptions").EnumerateArray().Select(feed => new RssFeed(
            feed.GetProperty("id").GetString()!,
            feed.GetProperty("title").GetString() ?? "",
            OptionalString(feed, "url"),
            OptionalString(feed, "htmlUrl"),
            [.. feed.TryGetProperty("categories", out JsonElement categories) ? categories.EnumerateArray().Select(category => category.GetProperty("label").GetString()!) : []]))]);
    }

    public async Task<Result<IReadOnlyList<RssCategory>>> ListCategoriesAsync(CancellationToken cancellationToken)
    {
        Result<JsonElement> json = await GetJsonAsync("reader/api/0/tag/list?output=json", cancellationToken);
        return json.Map<IReadOnlyList<RssCategory>>(root => [.. root.GetProperty("tags").EnumerateArray()
            .Where(tag => OptionalString(tag, "type") == "folder")
            .Select(tag => tag.GetProperty("id").GetString()!)
            .Where(id => id.StartsWith(LabelPrefix, StringComparison.Ordinal))
            .Select(id => new RssCategory(id, id[LabelPrefix.Length..]))]);
    }

    public Task<Result<IReadOnlyList<RssArticle>>> GetUnreadAsync(string? category, DateTimeOffset? since, int limit, CancellationToken cancellationToken)
    {
        string stream = category is null ? "user/-/state/com.google/reading-list" : LabelPrefix + Uri.EscapeDataString(category);
        string filters = $"&xt={Uri.EscapeDataString(ReadState)}" + (since is { } start ? $"&ot={start.ToUnixTimeSeconds()}" : "");
        return StreamAsync(stream, filters, limit, cancellationToken);
    }

    public async Task<Result<RssArticle>> GetArticleAsync(string id, CancellationToken cancellationToken)
    {
        if (!IsArticleId(id))
            return RssErrors.ArticleNotFound(id);

        Result<JsonElement> json = await SendJsonAsync(
            () => new HttpRequestMessage(HttpMethod.Post, "reader/api/0/stream/items/contents?output=json") { Content = new FormUrlEncodedContent([new("i", id)]) },
            cancellationToken);
        if (json.IsFailure)
            return json.Error;

        return json.Value.GetProperty("items").EnumerateArray().Select(ToArticle).FirstOrDefault(article => article.Id == id) is { } found
            ? found
            : RssErrors.ArticleNotFound(id);
    }

    public async Task<Result<IReadOnlyList<RssArticle>>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        string[] words = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        Result<IReadOnlyList<RssArticle>> recent = await StreamAsync("user/-/state/com.google/reading-list", "", _options.SearchWindow, cancellationToken);
        return recent.Map<IReadOnlyList<RssArticle>>(articles => [.. articles
            .Where(article => words.All(word => article.Title.Contains(word, StringComparison.OrdinalIgnoreCase) || article.Summary.Contains(word, StringComparison.OrdinalIgnoreCase)))
            .Take(limit)]);
    }

    public async Task<Result> MarkReadAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        if (ids.FirstOrDefault(id => !IsArticleId(id)) is { } invalid)
            return RssErrors.ArticleNotFound(invalid);
        if (ids.Count == 0)
            return Result.Success();

        Result<string> edited = await SendAsync(async ct =>
        {
            Result<string> token = await SendRawAsync(() => new HttpRequestMessage(HttpMethod.Get, "reader/api/0/token"), ct);
            if (token.IsFailure)
                return token;

            List<KeyValuePair<string, string>> form = [new("a", ReadState), new("T", token.Value.Trim()), .. ids.Select(id => new KeyValuePair<string, string>("i", id))];
            return await SendRawAsync(() => new HttpRequestMessage(HttpMethod.Post, "reader/api/0/edit-tag") { Content = new FormUrlEncodedContent(form) }, ct);
        }, cancellationToken);
        return edited.IsSuccess ? Result.Success() : edited.Error;
    }

    // Pages through a stream with the continuation token until `limit` articles or the end.
    private async Task<Result<IReadOnlyList<RssArticle>>> StreamAsync(string stream, string filters, int limit, CancellationToken cancellationToken)
    {
        var articles = new List<RssArticle>();
        string? continuation = null;
        while (articles.Count < limit)
        {
            int count = Math.Min(Math.Max(1, _options.PageSize), limit - articles.Count);
            string path = $"reader/api/0/stream/contents/{stream}?output=json&n={count}{filters}" + (continuation is null ? "" : $"&c={Uri.EscapeDataString(continuation)}");
            Result<JsonElement> page = await GetJsonAsync(path, cancellationToken);
            if (page.IsFailure)
                return page.Error;

            List<RssArticle> items = [.. page.Value.GetProperty("items").EnumerateArray().Select(ToArticle)];
            articles.AddRange(items);
            continuation = OptionalString(page.Value, "continuation");
            if (items.Count == 0 || continuation is null)
                break;
        }

        return articles.Take(limit).ToList();
    }

    private static RssArticle ToArticle(JsonElement item)
    {
        string[] categories = [.. item.GetProperty("categories").EnumerateArray().Select(category => category.GetString()!)];
        string? link = FirstHref(item, "canonical") ?? FirstHref(item, "alternate");
        string html = item.TryGetProperty("summary", out JsonElement summary) ? summary.GetProperty("content").GetString() ?? ""
            : item.TryGetProperty("content", out JsonElement content) ? content.GetProperty("content").GetString() ?? ""
            : "";
        return new RssArticle(
            DecimalId(item.GetProperty("id").GetString()!),
            item.TryGetProperty("title", out JsonElement title) ? title.GetString() ?? "" : "",
            item.TryGetProperty("origin", out JsonElement origin) ? OptionalString(origin, "title") ?? "" : "",
            Uri.TryCreate(link, UriKind.Absolute, out Uri? uri) ? uri : null,
            DateTimeOffset.FromUnixTimeSeconds(item.GetProperty("published").GetInt64()),
            HtmlText.Convert(html),
            !categories.Contains(ReadState),
            [.. categories.Where(category => category.StartsWith(LabelPrefix, StringComparison.Ordinal)).Select(category => category[LabelPrefix.Length..])]);
    }

    private static string DecimalId(string id) =>
        id.StartsWith(ItemPrefix, StringComparison.Ordinal)
            ? ulong.Parse(id[ItemPrefix.Length..], NumberStyles.HexNumber, CultureInfo.InvariantCulture).ToString(CultureInfo.InvariantCulture)
            : id;

    internal static bool IsArticleId(string id) => id.Length is > 0 and <= 20 && id.All(char.IsAsciiDigit) && id[0] != '0';

    private static string? FirstHref(JsonElement item, string name) =>
        item.TryGetProperty(name, out JsonElement links) && links.ValueKind == JsonValueKind.Array && links.GetArrayLength() > 0
            ? OptionalString(links[0], "href")
            : null;

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private Task<Result<JsonElement>> GetJsonAsync(string path, CancellationToken cancellationToken) =>
        SendJsonAsync(() => new HttpRequestMessage(HttpMethod.Get, path), cancellationToken);

    private async Task<Result<JsonElement>> SendJsonAsync(Func<HttpRequestMessage> request, CancellationToken cancellationToken)
    {
        Result<string> body = await SendAsync(ct => SendRawAsync(request, ct), cancellationToken);
        if (body.IsFailure)
            return body.Error;

        try
        {
            return JsonDocument.Parse(body.Value).RootElement.Clone();
        }
        catch (JsonException exception)
        {
            return RssErrors.Unreachable($"unexpected response ({exception.Message})");
        }
    }

    // Runs `work` with a valid auth token; a rejected token is renewed once.
    private async Task<Result<string>> SendAsync(Func<CancellationToken, Task<Result<string>>> work, CancellationToken cancellationToken)
    {
        if (!_options.IsConfigured)
            return RssErrors.NotConfigured();

        for (int attempt = 0; ; attempt++)
        {
            string? used = _auth;
            if (used is null)
            {
                Result login = await LoginAsync(null, cancellationToken);
                if (login.IsFailure)
                    return login.Error;
                used = _auth;
            }

            Result<string> result = await work(cancellationToken);
            if (result.IsSuccess || result.Error.Code != Unauthorized.Code || attempt > 0)
                return result.IsFailure && result.Error.Code == Unauthorized.Code ? RssErrors.AuthFailed() : result;

            await LoginAsync(used, cancellationToken);
        }
    }

    private async Task<Result<string>> SendRawAsync(Func<HttpRequestMessage> create, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = create();
        request.RequestUri = new Uri(_options.ApiRoot(), request.RequestUri!);
        request.Headers.Authorization = new AuthenticationHeaderValue("GoogleLogin", $"auth={_auth}");
        try
        {
            using HttpClient http = httpClients.CreateClient(HttpClientName);
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Unauthorized;
            if (!response.IsSuccessStatusCode)
                return RssErrors.Unreachable($"HTTP {(int)response.StatusCode}");
            return await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (HttpRequestException exception)
        {
            return RssErrors.Unreachable(exception.Message);
        }
    }

    // Logs in unless another caller already replaced the rejected token.
    private async Task<Result> LoginAsync(string? rejected, CancellationToken cancellationToken)
    {
        await _login.WaitAsync(cancellationToken);
        try
        {
            if (_auth is not null && _auth != rejected)
                return Result.Success();

            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(_options.ApiRoot(), "accounts/ClientLogin"))
            {
                Content = new FormUrlEncodedContent([new("Email", _options.UserName), new("Passwd", _options.ApiPassword!)]),
            };
            using HttpClient http = httpClients.CreateClient(HttpClientName);
            using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return RssErrors.AuthFailed();
            if (!response.IsSuccessStatusCode)
                return RssErrors.Unreachable($"HTTP {(int)response.StatusCode}");

            string body = await response.Content.ReadAsStringAsync(cancellationToken);
            _auth = body.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.StartsWith("Auth=", StringComparison.Ordinal))?[5..];
            return _auth is null ? RssErrors.AuthFailed() : Result.Success();
        }
        catch (HttpRequestException exception)
        {
            return RssErrors.Unreachable(exception.Message);
        }
        finally
        {
            _login.Release();
        }
    }
}
