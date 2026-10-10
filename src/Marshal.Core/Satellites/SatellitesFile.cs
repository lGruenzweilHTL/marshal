using Marshal.Abstractions;

namespace Marshal.Core.Satellites;

/// <summary>
/// Contents of <c>/config/satellites.yaml</c>, keyed by satellite entity id. Hot-reloaded.
/// </summary>
public sealed class SatellitesFile
{
    private static readonly string[] _verbosities = ["short", "normal", "detailed"];

    public Dictionary<string, SatelliteEntry> Satellites { get; set; } = new(StringComparer.Ordinal);

    public static Result Validate(SatellitesFile file) => Validate(file, [], []);

    /// <summary>
    /// Validates the profiles; a <c>display</c> must name one of <paramref name="displays"/>, a <c>speaker</c> one of
    /// <paramref name="speakers"/>.
    /// </summary>
    public static Result Validate(SatellitesFile file, IReadOnlyCollection<string> displays, IReadOnlyCollection<string> speakers)
    {
        foreach ((string id, SatelliteEntry entry) in file.Satellites)
        {
            if (!_verbosities.Contains(entry.Verbosity))
                return new Error("config.invalid", $"satellites.{id}.verbosity must be one of {string.Join(", ", _verbosities)}.", ErrorType.Validation);
            if (entry.Display is { } display && !displays.Contains(display))
                return new Error("config.invalid", $"satellites.{id}.display names no configured display '{display}' (displays.surfaces).", ErrorType.Validation);
            if (entry.Speaker is { } speaker && !speakers.Contains(speaker))
                return new Error("config.invalid", $"satellites.{id}.speaker names no configured speaker '{speaker}' (music.speakers).", ErrorType.Validation);
        }

        return Result.Success();
    }
}
