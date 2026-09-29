namespace Marshal.Abstractions.Rss;

/// <summary>Expected failures of the feed reader and the full-text extractor.</summary>
public static class RssErrors
{
    public static Error NotConfigured() => new("rss.not_configured", "No feed reader is configured.", ErrorType.Unavailable);

    public static Error Unreachable(string reason) => new("rss.unreachable", $"The feed reader could not be reached: {reason}", ErrorType.Unavailable);

    public static Error AuthFailed() => new("rss.auth_failed", "The feed reader rejected the user name or API password.", ErrorType.Unavailable);

    public static Error ArticleNotFound(string id) => new("rss.article_not_found", $"There is no article '{id}'.", ErrorType.NotFound);

    public static Error FullTextFailed(string reason) => new("rss.full_text_failed", $"The article page could not be read: {reason}", ErrorType.Unavailable);
}
