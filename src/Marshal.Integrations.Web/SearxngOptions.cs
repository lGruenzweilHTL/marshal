using Marshal.Abstractions.Secrets;

namespace Marshal.Integrations.Web;

/// <summary>
/// The <c>web.searxng</c> section of <c>marshal.yaml</c>. Web search is optional; with <see cref="Url"/> set the
/// <c>web.*</c> tools appear. Keep the SearXNG instance on the LAN or a VPN: exposed, it is an open search proxy.
/// </summary>
public sealed class SearxngOptions
{
    /// <summary>Configuration section.</summary>
    public const string Section = "web:searxng";

    /// <summary>Engines sent with every request when <see cref="Engines"/> is not set.</summary>
    public static IReadOnlyList<string> DefaultEngines { get; } = ["google", "bing", "duckduckgo", "wikipedia"];

    /// <summary>SearXNG base URL; JSON output must be enabled in its <c>settings.yml</c>.</summary>
    public Uri? Url { get; set; }

    /// <summary>Name of the secret holding an <c>Authorization</c> header value, for a SearXNG behind a reverse proxy.</summary>
    public string? AuthHeaderSecret { get; set; }

    /// <summary>The header value, resolved from the secret store at startup.</summary>
    [Secret(nameof(AuthHeaderSecret))]
    public string? AuthHeader { get; set; }

    /// <summary>SearXNG engine names; <see cref="DefaultEngines"/> when not set. A list so binding replaces, never appends.</summary>
    public List<string>? Engines { get; set; }

    /// <summary>Most results one search returns (1-20).</summary>
    public int MaxResults { get; set; } = 8;

    internal bool IsConfigured => Url is not null;

    internal IReadOnlyList<string> EffectiveEngines => Engines is { Count: > 0 } engines ? engines : DefaultEngines;
}
