using Marshal.Abstractions.Secrets;

namespace Marshal.Integrations.HomeAssistant;

/// <summary>
/// The <c>ha:</c> section of <c>marshal.yaml</c>. Home Assistant is optional; with <see cref="Url"/> set the token
/// secret is required.
/// </summary>
public sealed class HomeAssistantOptions
{
    /// <summary>Base URL of Home Assistant (e.g. <c>http://homeassistant.local:8123</c>), including a path base behind a proxy.</summary>
    public Uri? Url { get; set; }

    /// <summary>Name of the secret holding a long-lived access token.</summary>
    public string? TokenSecret { get; set; }

    /// <summary>The access token, resolved from the secret store at startup.</summary>
    [Secret(nameof(TokenSecret))]
    public string? Token { get; set; }

    /// <summary>The websocket endpoint below <see cref="Url"/>.</summary>
    public Uri WebSocketUri()
    {
        Uri url = Url ?? throw new InvalidOperationException("ha.url is not configured.");
        var builder = new UriBuilder(new Uri(EnsureTrailingSlash(url), "api/websocket"))
        {
            Scheme = url.Scheme == Uri.UriSchemeHttps ? "wss" : "ws",
        };
        return builder.Uri;
    }

    /// <summary><see cref="Url"/> with a trailing slash, so relative API paths keep a path base.</summary>
    public Uri BaseUri() => EnsureTrailingSlash(Url ?? throw new InvalidOperationException("ha.url is not configured."));

    private static Uri EnsureTrailingSlash(Uri url) =>
        url.AbsoluteUri.EndsWith('/') ? url : new Uri(url.AbsoluteUri + "/");
}
