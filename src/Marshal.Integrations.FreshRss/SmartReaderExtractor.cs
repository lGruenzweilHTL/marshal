using System.Net.Http.Headers;
using System.Text;
using Marshal.Abstractions;
using Marshal.Abstractions.Rss;
using Microsoft.Extensions.Options;
using SmartReader;

namespace Marshal.Integrations.FreshRss;

/// <summary>
/// Fetches an article page (http / https only, HTML only) within the configured size and time limits and extracts
/// its readable text with SmartReader (a port of Mozilla Readability). The page is never rendered or executed.
/// </summary>
public sealed class SmartReaderExtractor(IHttpClientFactory httpClients, IOptions<FreshRssOptions> options) : IFullTextExtractor
{
    public const string HttpClientName = "rss-fulltext";

    private readonly FullTextOptions _limits = options.Value.FullText;

    public async Task<Result<FullText>> ExtractAsync(Uri link, CancellationToken cancellationToken)
    {
        if (!link.IsAbsoluteUri || (link.Scheme != Uri.UriSchemeHttp && link.Scheme != Uri.UriSchemeHttps))
            return RssErrors.FullTextFailed("the article link is not an http(s) URL");

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_limits.Timeout);
        Result<string> html;
        try
        {
            html = await FetchAsync(link, timeout.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return RssErrors.FullTextFailed($"no answer within {_limits.Timeout.TotalSeconds:0} seconds");
        }
        catch (HttpRequestException exception)
        {
            return RssErrors.FullTextFailed(exception.Message);
        }

        if (html.IsFailure)
            return html.Error;

        Article article = new Reader(link.ToString(), html.Value).GetArticle();
        if (!article.IsReadable || string.IsNullOrWhiteSpace(article.TextContent))
            return RssErrors.FullTextFailed("no readable article text on the page");

        string text = Normalize(article.TextContent);
        bool truncated = text.Length > _limits.MaxCharacters;
        return new FullText(article.Title ?? "", truncated ? text[.._limits.MaxCharacters] : text, truncated);
    }

    private async Task<Result<string>> FetchAsync(Uri link, CancellationToken cancellationToken)
    {
        using HttpClient http = httpClients.CreateClient(HttpClientName);
        using var request = new HttpRequestMessage(HttpMethod.Get, link);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/html"));
        using HttpResponseMessage response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (!response.IsSuccessStatusCode)
            return RssErrors.FullTextFailed($"HTTP {(int)response.StatusCode}");

        string? mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && mediaType != "text/html" && mediaType != "application/xhtml+xml")
            return RssErrors.FullTextFailed($"the page is {mediaType}, not HTML");
        if (response.Content.Headers.ContentLength > _limits.MaxBytes)
            return TooLarge();

        await using Stream body = await response.Content.ReadAsStreamAsync(cancellationToken);
        var buffer = new MemoryStream();
        byte[] chunk = new byte[16 * 1024];
        int read;
        while ((read = await body.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > _limits.MaxBytes)
                return TooLarge();
            buffer.Write(chunk, 0, read);
        }

        Encoding encoding = Encoding.UTF8;
        if (response.Content.Headers.ContentType?.CharSet is { Length: > 0 } charset)
        {
            try
            {
                encoding = Encoding.GetEncoding(charset.Trim('"'));
            }
            catch (ArgumentException)
            {
                // Unknown charset: UTF-8 is the best guess.
            }
        }

        return encoding.GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private Error TooLarge() => RssErrors.FullTextFailed($"the page is larger than {_limits.MaxBytes / 1024} KB");

    // Readability keeps the page's line structure; collapse runs of blank lines and trailing spaces.
    private static string Normalize(string text)
    {
        string[] lines = [.. text.Split('\n').Select(line => string.Join(' ', line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))];
        return string.Join('\n', lines.Where((line, index) => line.Length > 0 || (index > 0 && lines[index - 1].Length > 0))).Trim();
    }
}
