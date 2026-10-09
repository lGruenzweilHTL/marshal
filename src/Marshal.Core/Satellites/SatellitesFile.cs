using Marshal.Abstractions;

namespace Marshal.Core.Satellites;

/// <summary>
/// Contents of <c>/config/satellites.yaml</c>, keyed by satellite entity id. Hot-reloaded.
/// </summary>
public sealed class SatellitesFile
{
    private static readonly string[] _verbosities = ["short", "normal", "detailed"];

    public Dictionary<string, SatelliteEntry> Satellites { get; set; } = new(StringComparer.Ordinal);

    public static Result Validate(SatellitesFile file) => Validate(file, []);

    /// <summary>Validates the profiles; a <c>display</c> must name one of <paramref name="displays"/>.</summary>
    public static Result Validate(SatellitesFile file, IReadOnlyCollection<string> displays)
    {
        foreach ((string id, SatelliteEntry entry) in file.Satellites)
        {
            if (!_verbosities.Contains(entry.Verbosity))
                return new Error("config.invalid", $"satellites.{id}.verbosity must be one of {string.Join(", ", _verbosities)}.", ErrorType.Validation);
            if (entry.Display is { } display && !displays.Contains(display))
                return new Error("config.invalid", $"satellites.{id}.display names no configured display '{display}' (displays.surfaces).", ErrorType.Validation);
        }

        return Result.Success();
    }
}
