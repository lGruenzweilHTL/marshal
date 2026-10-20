using System.Net.Http.Headers;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Web;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.Web;

/// <summary>
/// SearXNG's JSON API (<c>GET /search?q=…&amp;format=json&amp;engines=…&amp;language=…</c>). One request per search;
/// result pages are never fetched.
/// </summary>
internal sealed class SearxngSearchProvider(IHttpClientFactory clients, IOptions<SearxngOptions> options) : IWebSearchProvider
{
    public const string HttpClientName = "searxng";

    private readonly SearxngOptions _options = options.Value;

    public bool IsConfigured => _options.IsConfigured;

    public int MaxResults => _options.MaxResults;

    public async Task<Result<IReadOnlyList<WebResult>>> SearchAsync(string query, string language, int limit, CancellationToken cancellationToken)
    {
        if (_options.Url is not { } root)
            return Unavailable("Web search is not configured.");

        string baseUrl = root.ToString().TrimEnd('/');
        string url = $"{baseUrl}/search?q={Uri.EscapeDataString(query)}&format=json&engines={Uri.EscapeDataString(string.Join(',', _options.EffectiveEngines))}&language={Uri.EscapeDataString(language)}";
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (!string.IsNullOrEmpty(_options.AuthHeader))
            request.Headers.TryAddWithoutValidation("Authorization", _options.AuthHeader);

        try
        {
            using HttpResponseMessage response = await clients.CreateClient(HttpClientName).SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
                return Unavailable($"The search service answered {(int)response.StatusCode}.");

            await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
            using JsonDocument json = await JsonDocument.ParseAsync(body, cancellationToken: cancellationToken);
            return Result.Success<IReadOnlyList<WebResult>>(Map(json.RootElement, limit));
        }
        catch (HttpRequestException)
        {
            return Unavailable("The search service cannot be reached.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Unavailable("The search service did not answer in time.");
        }
        catch (JsonException)
        {
            return Unavailable("The search service did not answer with JSON. Is the json format enabled in its settings.yml?");
        }
    }

    private static List<WebResult> Map(JsonElement root, int limit)
    {
        if (!root.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
            return [];

        var mapped = new List<WebResult>();
        foreach (JsonElement result in results.EnumerateArray())
        {
            if (mapped.Count == limit)
                break;
            if (Text(result, "url") is not { } link || !Uri.TryCreate(link, UriKind.Absolute, out Uri? url) || (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
                continue;

            mapped.Add(new WebResult(Text(result, "title") ?? url.Host, Text(result, "content") ?? "", url));
        }

        return mapped;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString()?.Trim() : null;

    private static Error Unavailable(string message) => new("web.unavailable", message, ErrorType.Unavailable);
}
