namespace Marshal.Core.Identity;

/// <summary>
/// Instance-wide settings (<c>instance</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class InstanceOptions
{
    /// <summary>IANA time zone for new users. Unset: UTC (Home Assistant's time zone once the HA client exists).</summary>
    public string? TimeZone { get; set; }
}
