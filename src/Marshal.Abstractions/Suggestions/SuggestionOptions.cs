using System.Text.RegularExpressions;

namespace Marshal.Abstractions.Suggestions;

/// <summary>
/// The <c>suggestions</c> section of <c>marshal.yaml</c>: thresholds of the acceptance estimate, expiry and the
/// detectors' parameters. Suggestions run only for owners with "help Marshal learn" on.
/// </summary>
public sealed partial class SuggestionOptions
{
    public const string Section = "suggestions";

    /// <summary>The first this many candidates of a detector go to the inbox whatever the estimate.</summary>
    public int Explore { get; set; } = 3;

    /// <summary>Below this estimate a candidate is dropped.</summary>
    public double MinAcceptance { get; set; } = 0.3;

    /// <summary>At or above this estimate a candidate is pushed with priority normal.</summary>
    public double PushAcceptance { get; set; } = 0.6;

    /// <summary>An unanswered suggestion expires after this.</summary>
    public TimeSpan Expiry { get; set; } = TimeSpan.FromHours(6);

    /// <summary>Per detector id.</summary>
    public Dictionary<string, SuggestionDetectorOptions> Detectors { get; set; } = [];

    /// <summary>The detector's settings, defaults when it has none.</summary>
    public SuggestionDetectorOptions For(string detector) => Detectors.TryGetValue(detector, out SuggestionDetectorOptions? found) ? found : new();

    /// <summary>The first configuration error naming its key, or null when the section is valid.</summary>
    public string? Validate()
    {
        if (Explore < 0)
            return "suggestions.explore cannot be negative.";
        if (MinAcceptance is < 0 or > 1 || PushAcceptance is < 0 or > 1 || MinAcceptance > PushAcceptance)
            return "suggestions.minAcceptance and suggestions.pushAcceptance must be between 0 and 1, minAcceptance not above pushAcceptance.";
        if (Expiry <= TimeSpan.Zero)
            return "suggestions.expiry must be positive.";

        foreach ((string id, SuggestionDetectorOptions detector) in Detectors)
        {
            if (!DetectorId().IsMatch(id))
                return $"suggestions.detectors.{id}: a detector id must match [a-z][a-z0-9_]*.";
            if (detector.Cooldown < TimeSpan.Zero)
                return $"suggestions.detectors.{id}.cooldown cannot be negative.";
            if (detector.MinProbability is < 0 or > 100)
                return $"suggestions.detectors.{id}.minProbability must be between 0 and 100.";
            if (detector.Factor <= 1)
                return $"suggestions.detectors.{id}.factor must be greater than 1.";
        }

        return null;
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$", RegexOptions.CultureInvariant)]
    private static partial Regex DetectorId();
}

/// <summary>One detector's settings; each detector reads the keys it knows.</summary>
public sealed class SuggestionDetectorOptions
{
    public bool Enabled { get; set; } = true;

    /// <summary>A candidate with a dedupe key seen this recently is dropped.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromHours(12);

    /// <summary><c>window_rain</c>: precipitation probability in percent that counts as rain.</summary>
    public int MinProbability { get; set; } = 60;

    /// <summary><c>energy_anomaly</c>: yesterday's consumption at least this times the 14-day median.</summary>
    public double Factor { get; set; } = 1.5;

    /// <summary><c>window_rain</c>: the <c>weather.*</c> entity to read; null for the first one.</summary>
    public string? WeatherEntity { get; set; }
}
