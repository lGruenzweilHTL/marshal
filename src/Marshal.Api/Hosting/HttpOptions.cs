namespace Marshal.Api.Hosting;

/// <summary>
/// HTTP settings (<c>http</c>). The API version is never configurable; only the base path is.
/// </summary>
public sealed class HttpOptions
{
    /// <summary>Prefix of every route behind a reverse proxy, e.g. <c>/marshal</c>.</summary>
    public string? PathBase { get; set; }

    /// <summary>Kestrel listen URLs.</summary>
    public string Urls { get; set; } = "http://0.0.0.0:8080";
}
