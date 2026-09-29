namespace Marshal.Abstractions.Rss;

/// <summary>
/// Readable text of an article page. Callers pass only links that came from the feed reader, never a URL chosen by
/// the model; implementations enforce size and time limits.
/// </summary>
public interface IFullTextExtractor
{
    Task<Result<FullText>> ExtractAsync(Uri link, CancellationToken cancellationToken);
}

/// <summary>Extracted article text.</summary>
/// <param name="Title">Page title.</param>
/// <param name="Text">Readable text.</param>
/// <param name="Truncated">True when the text was cut to the length limit.</param>
public sealed record FullText(string Title, string Text, bool Truncated);
