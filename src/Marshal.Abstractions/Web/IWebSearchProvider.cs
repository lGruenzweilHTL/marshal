namespace Marshal.Abstractions.Web;

/// <summary>
/// Searches the web through a configured provider (SearXNG in v1). Results are written by strangers and are untrusted;
/// the provider never fetches result pages.
/// </summary>
public interface IWebSearchProvider
{
    /// <summary>False until the provider is configured; the <c>web.*</c> tools stay hidden until then.</summary>
    bool IsConfigured { get; }

    /// <summary>Largest number of results one search returns (<c>web.searxng.maxResults</c>).</summary>
    int MaxResults { get; }

    /// <summary>One search request. Fails with <c>web.unavailable</c> when the provider cannot answer.</summary>
    Task<Result<IReadOnlyList<WebResult>>> SearchAsync(string query, string language, int limit, CancellationToken cancellationToken);
}

/// <summary>One search hit.</summary>
/// <param name="Title">Page title as the engine reports it.</param>
/// <param name="Snippet">Short excerpt as the engine reports it.</param>
/// <param name="Url">Result URL; shown, never fetched.</param>
public sealed record WebResult(string Title, string Snippet, Uri Url)
{
    /// <summary>The host of <see cref="Url"/> without a leading <c>www.</c>.</summary>
    public string Domain => Url.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? Url.Host[4..] : Url.Host;
}
