namespace Marshal.Integrations.Media;

/// <summary>
/// The <c>media</c> section of <c>marshal.yaml</c>: Seerr for search and requests, optionally Sonarr and Radarr for
/// calendars and download queues, and the Jellyfin web URL for links. The <c>media.*</c> tools appear once
/// <c>media.seerr.url</c> is set.
/// </summary>
public sealed class MediaOptions
{
    public MediaService? Seerr { get; set; }

    public MediaService? Sonarr { get; set; }

    public MediaService? Radarr { get; set; }

    /// <summary>Base URL of the Jellyfin web client, e.g. <c>https://jellyfin.lan/</c>; links are built from it.</summary>
    public Uri? JellyfinUrl { get; set; }

    internal bool IsConfigured => Seerr?.Url is not null;

    internal MediaService? For(ArrKind kind) => kind == ArrKind.Sonarr ? Sonarr : Radarr;
}

/// <summary>One service: its base URL and the name of the secret holding its API key.</summary>
public sealed class MediaService
{
    public Uri? Url { get; set; }

    /// <summary>Name of the secret holding the API key; read per request.</summary>
    public string? ApiKeySecret { get; set; }
}
