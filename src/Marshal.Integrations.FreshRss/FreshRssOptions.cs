using Marshal.Abstractions.Secrets;

namespace Marshal.Integrations.FreshRss;

/// <summary>
/// The <c>freshrss:</c> section of <c>marshal.yaml</c>. FreshRSS is optional; with <see cref="Url"/> set the RSS tools
/// appear. The API password is the one set in FreshRSS under Profile → API management, not the login password.
/// </summary>
public sealed class FreshRssOptions
{
    /// <summary>FreshRSS base URL (the API lives at <c>api/greader.php</c> below it).</summary>
    public Uri? Url { get; set; }

    public string UserName { get; set; } = "";

    /// <summary>Name of the secret holding the API password.</summary>
    public string? ApiPasswordSecret { get; set; }

    /// <summary>The API password, resolved from the secret store at startup.</summary>
    [Secret(nameof(ApiPasswordSecret))]
    public string? ApiPassword { get; set; }

    /// <summary>How many recent articles (read and unread) <c>rss.search</c> looks through; the API has no search.</summary>
    public int SearchWindow { get; set; } = 500;

    /// <summary>Articles per API request; larger requests follow <c>continuation</c>.</summary>
    public int PageSize { get; set; } = 100;

    /// <summary>Limits for <c>rss.fetch_full_text</c>.</summary>
    public FullTextOptions FullText { get; set; } = new();

    internal bool IsConfigured => Url is not null && UserName.Length > 0 && !string.IsNullOrEmpty(ApiPassword);

    /// <summary>The Google Reader API root with a trailing slash.</summary>
    internal Uri ApiRoot()
    {
        Uri url = Url ?? throw new InvalidOperationException("freshrss.url is not configured.");
        string text = url.ToString();
        return new Uri((text.EndsWith('/') ? text : text + "/") + "api/greader.php/");
    }
}

/// <summary>Limits for fetching and extracting article pages.</summary>
public sealed class FullTextOptions
{
    /// <summary>Largest page that is read, in bytes.</summary>
    public int MaxBytes { get; set; } = 2 * 1024 * 1024;

    /// <summary>Longest time a page fetch may take.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Longest text returned to the model, in characters.</summary>
    public int MaxCharacters { get; set; } = 20_000;
}
