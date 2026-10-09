namespace Marshal.Abstractions.Displays;

/// <summary>The configured displays and which one belongs to a satellite's room.</summary>
public interface IDisplayDirectory
{
    /// <summary>The display named in the satellite's profile; null without satellite, profile or display.</summary>
    DisplaySurface? ForSatellite(string? satelliteId);
}
