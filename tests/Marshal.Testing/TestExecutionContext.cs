using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;

namespace Marshal.Testing;

/// <summary>
/// Execution contexts and owners for tests. Owner B exists to prove there is no data leakage.
/// </summary>
public static class TestExecutionContext
{
    /// <summary>First fake owner.</summary>
    public static readonly OwnerId OwnerA = new(Guid.Parse("00000000-0000-7000-8000-00000000000a"));

    /// <summary>Second fake owner.</summary>
    public static readonly OwnerId OwnerB = new(Guid.Parse("00000000-0000-7000-8000-00000000000b"));

    /// <summary>A text turn for <paramref name="owner"/> (default owner A).</summary>
    public static MarshalExecutionContext Text(OwnerId? owner = null, string language = "en") => new()
    {
        OwnerId = owner ?? OwnerA,
        Source = SessionSource.Api,
        InputMode = InputMode.Text,
        Language = language,
    };

    /// <summary>A voice turn from a satellite for <paramref name="owner"/> (default owner A).</summary>
    public static MarshalExecutionContext Voice(OwnerId? owner = null, string language = "en", string? satelliteId = "assist_satellite.kitchen", string? areaId = "kitchen") => new()
    {
        OwnerId = owner ?? OwnerA,
        Source = SessionSource.Voice,
        InputMode = InputMode.Voice,
        Language = language,
        SatelliteId = satelliteId,
        AreaId = areaId,
    };
}
