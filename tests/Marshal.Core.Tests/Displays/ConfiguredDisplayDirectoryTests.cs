using Marshal.Abstractions.Displays;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Satellites;
using Marshal.Core.Displays;

namespace Marshal.Core.Tests.Displays;

public sealed class ConfiguredDisplayDirectoryTests
{
    private static readonly ConfiguredDisplayDirectory _directory = new(
        Microsoft.Extensions.Options.Options.Create(DisplayOptionsValidatorTests.Options(("living_tv", "media_player.living_room_tv"))),
        new Profiles(new Dictionary<string, SatelliteProfile>
        {
            ["assist_satellite.living_room"] = SatelliteProfile.Default with { Display = "living_tv" },
            ["assist_satellite.kitchen"] = SatelliteProfile.Default,
            ["assist_satellite.stale"] = SatelliteProfile.Default with { Display = "removed_tv" },
        }));

    [Fact]
    public void ForSatellite_WithDisplay_ReturnsSurface()
    {
        Assert.Equal(new DisplaySurface("living_tv", "media_player.living_room_tv"), _directory.ForSatellite("assist_satellite.living_room"));
    }

    [Theory]
    [InlineData("assist_satellite.kitchen")]
    [InlineData("assist_satellite.unknown")]
    [InlineData("assist_satellite.stale")]
    [InlineData(null)]
    public void ForSatellite_WithoutDisplay_ReturnsNull(string? satellite)
    {
        Assert.Null(_directory.ForSatellite(satellite));
    }

    private sealed class Profiles(Dictionary<string, SatelliteProfile> profiles) : ISatelliteContextProvider
    {
        public SatelliteProfile GetProfile(string? satelliteId) =>
            satelliteId is not null && profiles.TryGetValue(satelliteId, out SatelliteProfile? profile) ? profile : SatelliteProfile.Default;

        public Task<SatelliteContext> GetContextAsync(MarshalExecutionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
