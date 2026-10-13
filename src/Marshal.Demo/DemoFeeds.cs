using Marshal.Abstractions.Rss;

namespace Marshal.Demo;

/// <summary>The simulated feed reader: feeds, categories, articles with their full text, and read marks.</summary>
public sealed class DemoFeeds
{
    private readonly Lock _gate = new();
    private readonly List<RssArticle> _articles;
    private readonly Dictionary<string, string> _texts;

    internal DemoFeeds(DateTimeOffset started)
    {
        RssFile file = DemoResources.Read<RssFile>("rss.json");
        Categories = [.. file.Categories.Select(category => new RssCategory(category.Id, category.Label))];
        Feeds = [.. file.Feeds.Select(feed => new RssFeed(feed.Id, feed.Title, feed.Url, feed.SiteUrl, feed.Categories))];
        _articles = [.. file.Articles.Select(article =>
        {
            RssFeed feed = Feeds.Single(candidate => candidate.Id == article.Feed);
            return new RssArticle(article.Id, article.Title, feed.Title, new Uri(article.Link), started.AddMinutes(-article.MinutesAgo), article.Summary, article.Unread, feed.Categories);
        })];
        _texts = file.Articles.ToDictionary(article => article.Link, article => article.Text);
    }

    public IReadOnlyList<RssCategory> Categories { get; }

    public IReadOnlyList<RssFeed> Feeds { get; }

    public IReadOnlyList<RssArticle> Articles
    {
        get
        {
            lock (_gate)
                return [.. _articles];
        }
    }

    /// <summary>The full text behind an article link, or null for a link outside the demo feeds.</summary>
    public string? FullText(Uri link) => _texts.GetValueOrDefault(link.AbsoluteUri);

    public void MarkRead(IReadOnlyCollection<string> ids)
    {
        lock (_gate)
        {
            for (int index = 0; index < _articles.Count; index++)
            {
                if (ids.Contains(_articles[index].Id))
                    _articles[index] = _articles[index] with { Unread = false };
            }
        }
    }

    private sealed record RssFile(List<CategoryFile> Categories, List<FeedFile> Feeds, List<ArticleFile> Articles);

    private sealed record CategoryFile(string Id, string Label);

    private sealed record FeedFile(string Id, string Title, string? Url, string? SiteUrl, List<string> Categories);

    private sealed record ArticleFile(string Id, string Feed, string Title, int MinutesAgo, bool Unread, string Link, string Summary, string Text);
}
