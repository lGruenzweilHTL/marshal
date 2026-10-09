namespace Marshal.Core.Displays;

/// <summary>The <c>displays</c> section of <c>marshal.yaml</c>: screens that play artifact renditions.</summary>
public sealed class DisplayOptions
{
    /// <summary>Absolute URL at which displays reach Marshal; <c>notifications.publicUrl</c> when unset.</summary>
    public Uri? MediaBaseUrl { get; set; }

    /// <summary>Lifetime of display links in minutes.</summary>
    public int LinkMinutes { get; set; } = 240;

    /// <summary>Displays by name.</summary>
    public Dictionary<string, DisplaySurfaceOptions> Surfaces { get; set; } = new(StringComparer.Ordinal);
}

/// <summary>One display in <c>displays.surfaces</c>.</summary>
public sealed class DisplaySurfaceOptions
{
    /// <summary>Home Assistant <c>media_player</c> entity id.</summary>
    public string Entity { get; set; } = "";
}
