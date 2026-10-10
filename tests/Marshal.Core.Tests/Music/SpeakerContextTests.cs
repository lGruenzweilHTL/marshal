using Marshal.Abstractions.Context;
using Marshal.Abstractions.Satellites;
using Marshal.Core.Context;
using Marshal.Testing;

namespace Marshal.Core.Tests.Music;

public sealed class SpeakerContextTests
{
    private static readonly ConfiguredSpeakerDirectoryTests.Profiles _profiles = new(new Dictionary<string, SatelliteProfile>
    {
        ["assist_satellite.kitchen"] = SatelliteProfile.Default with { Speaker = "kitchen" },
    });

    [Fact]
    public async Task SatelliteWithSpeaker_NamesRoomSpeakerAndAll()
    {
        string? text = await Contribute(Speakers(), "assist_satellite.kitchen");

        Assert.Contains("Music speaker in this room: kitchen. Speakers: kitchen, living_room.", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SatelliteWithoutSpeaker_NamesOnly()
    {
        string? text = await Contribute(Speakers(), "assist_satellite.office");

        Assert.Contains("Music speakers: kitchen, living_room.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("in this room", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoSpeakersConfigured_NoLine()
    {
        string? text = await Contribute(new FakeSpeakerDirectory(), "assist_satellite.kitchen");

        Assert.Null(text);
    }

    private static FakeSpeakerDirectory Speakers()
    {
        var speakers = new FakeSpeakerDirectory(("kitchen", "media_player.kitchen_ma"), ("living_room", "media_player.sonos"));
        speakers.Satellites["assist_satellite.kitchen"] = "kitchen";
        return speakers;
    }

    private static Task<string?> Contribute(FakeSpeakerDirectory speakers, string satellite) =>
        new SatelliteContextContributor(new Satellites(_profiles), speakers)
            .ContributeAsync(new ContextRequest(TestExecutionContext.Voice(satelliteId: satellite), "x"), TestContext.Current.CancellationToken);

    private sealed class Satellites(ISatelliteContextProvider profiles) : ISatelliteContextProvider
    {
        public SatelliteProfile GetProfile(string? satelliteId) => profiles.GetProfile(satelliteId);

        public Task<SatelliteContext> GetContextAsync(Abstractions.Execution.MarshalExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult(new SatelliteContext(context.SatelliteId, GetProfile(context.SatelliteId), null, null));
    }
}
