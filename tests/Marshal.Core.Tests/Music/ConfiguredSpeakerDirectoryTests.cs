using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Music;
using Marshal.Abstractions.Satellites;
using Marshal.Core.Music;

namespace Marshal.Core.Tests.Music;

public sealed class ConfiguredSpeakerDirectoryTests
{
    private static readonly ConfiguredSpeakerDirectory _directory = new(
        Microsoft.Extensions.Options.Options.Create(MusicOptionsValidatorTests.Options(("kitchen", "media_player.kitchen_ma"), ("living_room", "media_player.sonos"))),
        new Profiles(new Dictionary<string, SatelliteProfile>
        {
            ["assist_satellite.kitchen"] = SatelliteProfile.Default with { Speaker = "kitchen" },
            ["assist_satellite.office"] = SatelliteProfile.Default,
            ["assist_satellite.stale"] = SatelliteProfile.Default with { Speaker = "removed" },
        }));

    [Fact]
    public void ForSatellite_WithSpeaker_ReturnsSpeaker()
    {
        Assert.Equal(new MusicSpeaker("kitchen", "media_player.kitchen_ma"), _directory.ForSatellite("assist_satellite.kitchen"));
    }

    [Theory]
    [InlineData("assist_satellite.office")]
    [InlineData("assist_satellite.unknown")]
    [InlineData("assist_satellite.stale")]
    [InlineData(null)]
    public void ForSatellite_WithoutSpeaker_ReturnsNull(string? satellite)
    {
        Assert.Null(_directory.ForSatellite(satellite));
    }

    [Fact]
    public void ByName_KnownAndUnknown()
    {
        Assert.Equal(new MusicSpeaker("living_room", "media_player.sonos"), _directory.ByName("living_room"));
        Assert.Null(_directory.ByName("media_player.sonos"));
        Assert.Null(_directory.ByName("garage"));
    }

    [Fact]
    public void Names_AreSorted()
    {
        Assert.Equal(["kitchen", "living_room"], _directory.Names);
    }

    internal sealed class Profiles(Dictionary<string, SatelliteProfile> profiles) : ISatelliteContextProvider
    {
        public SatelliteProfile GetProfile(string? satelliteId) =>
            satelliteId is not null && profiles.TryGetValue(satelliteId, out SatelliteProfile? profile) ? profile : SatelliteProfile.Default;

        public Task<SatelliteContext> GetContextAsync(MarshalExecutionContext context, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
