namespace Marshal.Core.Satellites;

/// <summary>
/// One satellite in <c>satellites.yaml</c>.
/// </summary>
public sealed class SatelliteEntry
{
    public string Verbosity { get; set; } = "normal";

    public string? Notes { get; set; }

    public List<string> DenyTools { get; set; } = [];

    public List<string> DenyConfirm { get; set; } = [];

    /// <summary>Name of the display in the satellite's room (<c>displays.surfaces</c>).</summary>
    public string? Display { get; set; }

    /// <summary>Name of the music speaker in the satellite's room (<c>music.speakers</c>).</summary>
    public string? Speaker { get; set; }
}
