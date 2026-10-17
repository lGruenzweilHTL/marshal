using Marshal.Integrations.HomeAssistant.Music;
using Marshal.Testing;

namespace Marshal.Integrations.HomeAssistant.Tests;

public sealed class HaNamespaceHintsTests
{
    private readonly FixtureCache _cache = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData("turn off the bed light")]
    [InlineData("Ist das Nachtlicht noch an?")]
    [InlineData("how warm is it in the kitchen")]
    public async Task Ha_EntityAliasOrAreaNamed_HintsHa(string text)
    {
        IReadOnlyList<string> hints = await new HaNamespaceHints(_cache).GetHintsAsync(text, TestExecutionContext.Text(), Ct);

        Assert.Equal(["ha"], hints);
    }

    [Theory]
    [InlineData("What is the capital of Austria?")]
    [InlineData("Wie spät ist es?")]
    public async Task Ha_NothingNamed_NoHint(string text)
    {
        Assert.Empty(await new HaNamespaceHints(_cache).GetHintsAsync(text, TestExecutionContext.Text(), Ct));
    }

    [Fact]
    public async Task Ha_CacheNotReady_NoHint()
    {
        _cache.Current = null;

        Assert.Empty(await new HaNamespaceHints(_cache).GetHintsAsync("turn off the bed light", TestExecutionContext.Text(), Ct));
    }

    [Theory]
    [InlineData("play some jazz in the living room")]
    [InlineData("Spiel etwas Jazz im Wohnzimmer")]
    [InlineData("pause the kitchen")]
    public async Task Music_SpeakerNamed_HintsMusic(string text)
    {
        var speakers = new FakeSpeakerDirectory(("kitchen", "media_player.kitchen_ma"), ("living_room", "media_player.living_room_sonos"), ("wohnzimmer", "media_player.wohnzimmer"));

        IReadOnlyList<string> hints = await new MusicNamespaceHints(speakers, _cache).GetHintsAsync(text, TestExecutionContext.Voice(), Ct);

        Assert.Equal(["music"], hints);
    }

    [Theory]
    [InlineData("what's on my calendar")]
    [InlineData("Wie wird das Wetter morgen?")]
    public async Task Music_NoSpeakerNamed_NoHint(string text)
    {
        var speakers = new FakeSpeakerDirectory(("kitchen", "media_player.kitchen_ma"), ("living_room", "media_player.living_room_sonos"));

        Assert.Empty(await new MusicNamespaceHints(speakers, _cache).GetHintsAsync(text, TestExecutionContext.Voice(), Ct));
    }
}
