using Marshal.Abstractions;
using Marshal.Abstractions.Extraction;
using Marshal.Abstractions.Rss;

namespace Marshal.Integrations.FreshRss.Tools;

/// <summary>
/// <c>rss:&lt;article id&gt;</c> for <c>agent.extract</c>: the article's full text from its web page when it can be
/// extracted, its feed content otherwise (taint source <c>rss</c>). The link comes from the feed reader, never from the
/// caller.
/// </summary>
public sealed class RssContentResolver(IRssClient client, IFullTextExtractor extractor) : IUntrustedContentResolver
{
    public string Kind => "rss";

    public ValueTask<bool> IsAvailableAsync(CancellationToken cancellationToken) => ValueTask.FromResult(client.IsConfigured);

    public async Task<Result<UntrustedItem>> ResolveAsync(string id, CancellationToken cancellationToken)
    {
        if (!FreshRssClient.IsArticleId(id))
            return NotFound(id);

        Result<RssArticle> article = await client.GetArticleAsync(id, cancellationToken);
        if (article.IsFailure)
            return NotFound(id);

        string body = article.Value.Summary;
        if (article.Value.Link is { } link && await extractor.ExtractAsync(link, cancellationToken) is { IsSuccess: true } full)
            body = full.Value.Text;

        return new UntrustedItem($"{Kind}:{id}", $"Title: {article.Value.Title}\nFeed: {article.Value.FeedTitle}\nPublished: {article.Value.Published:yyyy-MM-dd HH:mm zzz}\n\n{body}", "rss");
    }

    private static Error NotFound(string id) => new(ExtractionAnswer.NotFoundCode, $"There is no article '{id}'.", ErrorType.NotFound);
}
