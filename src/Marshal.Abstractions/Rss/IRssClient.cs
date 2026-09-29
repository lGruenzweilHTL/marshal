namespace Marshal.Abstractions.Rss;

/// <summary>
/// The feed reader (FreshRSS in v1). Reading and marking read only: there is no subscribe, unsubscribe or delete.
/// Article content comes from feeds and is untrusted.
/// </summary>
public interface IRssClient
{
    /// <summary>False when no feed reader is configured; the RSS tools are hidden then.</summary>
    bool IsConfigured { get; }

    Task<Result<IReadOnlyList<RssFeed>>> ListFeedsAsync(CancellationToken cancellationToken);

    Task<Result<IReadOnlyList<RssCategory>>> ListCategoriesAsync(CancellationToken cancellationToken);

    /// <summary>Unread articles, newest first, optionally in one category and crawled after <paramref name="since"/>.</summary>
    Task<Result<IReadOnlyList<RssArticle>>> GetUnreadAsync(string? category, DateTimeOffset? since, int limit, CancellationToken cancellationToken);

    /// <summary>One article; <c>rss.article_not_found</c> when the id is unknown.</summary>
    Task<Result<RssArticle>> GetArticleAsync(string id, CancellationToken cancellationToken);

    /// <summary>Recent articles (read and unread) whose title or summary contains every word of <paramref name="query"/>.</summary>
    Task<Result<IReadOnlyList<RssArticle>>> SearchAsync(string query, int limit, CancellationToken cancellationToken);

    Task<Result> MarkReadAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken);
}

/// <summary>A subscribed feed.</summary>
/// <param name="Id">Feed id (e.g. <c>feed/2</c>).</param>
/// <param name="Title">Feed title.</param>
/// <param name="Url">Feed URL.</param>
/// <param name="SiteUrl">Website of the feed.</param>
/// <param name="Categories">Category labels.</param>
public sealed record RssFeed(string Id, string Title, string? Url, string? SiteUrl, IReadOnlyList<string> Categories);

/// <summary>A category (folder) of feeds.</summary>
/// <param name="Id">Stream id (e.g. <c>user/-/label/Tech</c>).</param>
/// <param name="Label">Display name, also the tools' <c>category</c> argument.</param>
public sealed record RssCategory(string Id, string Label);

/// <summary>An article.</summary>
/// <param name="Id">Article id (decimal).</param>
/// <param name="Title">Title.</param>
/// <param name="FeedTitle">Title of the feed it came from.</param>
/// <param name="Link">The article's web page, as the feed reader stores it.</param>
/// <param name="Published">Publication time.</param>
/// <param name="Summary">Feed content as plain text.</param>
/// <param name="Unread">True while unread.</param>
/// <param name="Categories">Category labels.</param>
public sealed record RssArticle(string Id, string Title, string FeedTitle, Uri? Link, DateTimeOffset Published, string Summary, bool Unread, IReadOnlyList<string> Categories);
