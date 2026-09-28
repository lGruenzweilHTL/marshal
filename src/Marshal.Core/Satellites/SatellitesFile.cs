using Marshal.Abstractions;

namespace Marshal.Core.Satellites;

/// <summary>
/// Contents of <c>/config/satellites.yaml</c>, keyed by satellite entity id. Hot-reloaded.
/// </summary>
public sealed class SatellitesFile
{
    private static readonly string[] _verbosities = ["short", "normal", "detailed"];

    public Dictionary<string, SatelliteEntry> Satellites { get; set; } = new(StringComparer.Ordinal);

    public static Result Validate(SatellitesFile file)
    {
        foreach ((string id, SatelliteEntry entry) in file.Satellites)
        {
            if (!_verbosities.Contains(entry.Verbosity))
                return new Error("config.invalid", $"satellites.{id}.verbosity must be one of {string.Join(", ", _verbosities)}.", ErrorType.Validation);
        }

        return Result.Success();
    }
}
