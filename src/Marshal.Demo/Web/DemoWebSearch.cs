using Marshal.Abstractions;
using Marshal.Abstractions.Web;

namespace Marshal.Demo.Web;

/// <summary>
/// Fixed search results while no SearXNG is configured; a configured one is used as is. Never contacts a network host:
/// the result URLs are shown, never fetched.
/// </summary>
internal sealed class DemoWebSearch(IWebSearchProvider inner) : IWebSearchProvider
{
    private static readonly IReadOnlyList<WebResult> _results =
    [
        new("Christmas market at the main square – opening times", "The Christmas market on the main square opens on 20 November and runs daily from 10:00 to 21:00 until 24 December.", new Uri("https://www.example.org/christmas-market")),
        new("Christmas markets – Wikipedia", "Christmas markets are street markets associated with the celebration of Christmas during the four weeks of Advent.", new Uri("https://en.wikipedia.org/wiki/Christmas_market")),
        new("Voron 2.4 build guide", "Step-by-step assembly of the Voron 2.4 CoreXY printer, including frame, gantry and wiring.", new Uri("https://docs.vorondesign.com/build/")),
        new("Klipper input shaping", "Input shaping reduces ringing. Measure resonances with an ADXL345 and choose the shaper with SHAPER_CALIBRATE.", new Uri("https://www.klipper3d.org/Resonance_Compensation.html")),
    ];

    /// <summary>True when the real provider is configured; the demo then stays out of the way.</summary>
    public bool IsReal => inner.IsConfigured;

    public bool IsConfigured => true;

    public int MaxResults => IsReal ? inner.MaxResults : 8;

    public Task<Result<IReadOnlyList<WebResult>>> SearchAsync(string query, string language, int limit, CancellationToken cancellationToken) =>
        IsReal ? inner.SearchAsync(query, language, limit, cancellationToken) : Task.FromResult(Result.Success<IReadOnlyList<WebResult>>([.. _results.Take(limit)]));
}
