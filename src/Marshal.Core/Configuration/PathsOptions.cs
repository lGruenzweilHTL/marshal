namespace Marshal.Core.Configuration;

/// <summary>
/// Volume locations (<c>paths</c>). Usually set through <c>MARSHAL__Paths__*</c>.
/// </summary>
public sealed class PathsOptions
{
    /// <summary>Human-edited configuration (git-friendly, no secrets).</summary>
    public string Config { get; set; } = "/config";

    /// <summary>Database, Data Protection keys, backups.</summary>
    public string Data { get; set; } = "/data";

    /// <summary>Docker secrets directory.</summary>
    public string Secrets { get; set; } = "/run/secrets";
}
