using Marshal.Abstractions;
using Marshal.Abstractions.Rss;

namespace Marshal.Demo.Rss;

/// <summary>
/// The demo feed reader from <see cref="DemoWorld.Feeds"/> while no feed reader is configured; a configured one is
/// used as is.
/// </summary>
internal sealed class DemoRssClient(IRssClient inner, DemoWorld world) : IRssClient
{
    private DemoFeeds Feeds => world.Feeds;

    /// <summary>True when the real client is configured; the demo then stays out of the way.</summary>
    public bool IsReal => inner.IsConfigured;

    public bool IsConfigured => true;

    public Task<Result<IReadOnlyList<RssFeed>>> ListFeedsAsync(CancellationToken cancellationToken) =>
        IsReal ? inner.ListFeedsAsync(cancellationToken) : Task.FromResult(Result.Success(Feeds.Feeds));

    public Task<Result<IReadOnlyList<RssCategory>>> ListCategoriesAsync(CancellationToken cancellationToken) =>
        IsReal ? inner.ListCategoriesAsync(cancellationToken) : Task.FromResult(Result.Success(Feeds.Categories));

    public Task<Result<IReadOnlyList<RssArticle>>> GetUnreadAsync(string? category, DateTimeOffset? since, int limit, CancellationToken cancellationToken)
    {
        if (IsReal)
            return inner.GetUnreadAsync(category, since, limit, cancellationToken);

        return Found(Feeds.Articles
            .Where(article => article.Unread
                && (category is null || article.Categories.Contains(category, StringComparer.OrdinalIgnoreCase))
                && (since is null || article.Published > since)), limit);
    }

    public Task<Result<RssArticle>> GetArticleAsync(string id, CancellationToken cancellationToken)
    {
        if (IsReal)
            return inner.GetArticleAsync(id, cancellationToken);

        return Task.FromResult(Feeds.Articles.FirstOrDefault(article => article.Id == id) is { } found ? found : Result.Failure<RssArticle>(RssErrors.ArticleNotFound(id)));
    }

    public Task<Result<IReadOnlyList<RssArticle>>> SearchAsync(string query, int limit, CancellationToken cancellationToken)
    {
        if (IsReal)
            return inner.SearchAsync(query, limit, cancellationToken);

        string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return Found(Feeds.Articles.Where(article => words.All(word =>
            article.Title.Contains(word, StringComparison.OrdinalIgnoreCase) || article.Summary.Contains(word, StringComparison.OrdinalIgnoreCase))), limit);
    }

    public Task<Result> MarkReadAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken)
    {
        if (IsReal)
            return inner.MarkReadAsync(ids, cancellationToken);

        Feeds.MarkRead(ids);
        return Task.FromResult(Result.Success());
    }

    private static Task<Result<IReadOnlyList<RssArticle>>> Found(IEnumerable<RssArticle> articles, int limit) =>
        Task.FromResult(Result.Success<IReadOnlyList<RssArticle>>([.. articles.OrderByDescending(article => article.Published).Take(Math.Max(1, limit))]));
}

/// <summary>Full text of the demo articles; every other link goes to the real extractor while FreshRSS is real.</summary>
internal sealed class DemoFullTextExtractor(IFullTextExtractor inner, IRssClient client, DemoWorld world) : IFullTextExtractor
{
    public Task<Result<FullText>> ExtractAsync(Uri link, CancellationToken cancellationToken)
    {
        if (client is DemoRssClient { IsReal: false })
        {
            return Task.FromResult(world.Feeds.FullText(link) is { } text
                ? new FullText(world.Feeds.Articles.First(article => article.Link == link).Title, text, Truncated: false)
                : Result.Failure<FullText>(RssErrors.FullTextFailed("the page is not part of the demo feeds.")));
        }

        return inner.ExtractAsync(link, cancellationToken);
    }
}
