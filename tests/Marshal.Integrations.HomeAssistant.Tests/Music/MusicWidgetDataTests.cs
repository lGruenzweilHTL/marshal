using System.Text.Json;
using Marshal.Abstractions.Tools;
using Marshal.Abstractions.Widgets;
using Marshal.Integrations.HomeAssistant.Music;
using Marshal.Testing;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Tests.Music;

public sealed class MusicWidgetDataTests
{
    private readonly MusicToolsTests.FakeMusicPlayer _player = new();
    private readonly FakeSpeakerDirectory _speakers = new(("kitchen", "media_player.kitchen_ma"));

    [Fact]
    public async Task Queue_DataFromTheSpeakersQueue()
    {
        _player.Queue = new MusicQueueState("playing", new MusicItem(MusicMediaType.Track, "Innuendo", ["Queen"], "Innuendo", "spotify://track/1"), new MusicItem(MusicMediaType.Track, "I'm Going Slightly Mad", ["Queen"], "Innuendo", null), 12, false, "off");

        JsonElement? data = await new MusicWidgetData(_player, _speakers).GetAsync(Request("""{"speaker":"kitchen"}"""), Ct);

        Assert.Equal("kitchen", data!.Value.GetProperty("speaker").GetString());
        Assert.Equal("playing", data.Value.GetProperty("state").GetString());
        Assert.Equal("Innuendo", data.Value.GetProperty("current").GetProperty("name").GetString());
        Assert.Equal("Queen", data.Value.GetProperty("current").GetProperty("artists")[0].GetString());
        Assert.Equal("I'm Going Slightly Mad", data.Value.GetProperty("next").GetProperty("name").GetString());
        Assert.Equal(12, data.Value.GetProperty("items").GetInt32());
    }

    [Fact]
    public async Task UnknownSpeakerOrFailure_NoWidget()
    {
        Assert.Null(await new MusicWidgetData(_player, _speakers).GetAsync(Request("""{"speaker":"garage"}"""), Ct));
        _player.Failure = new Marshal.Abstractions.Error("music.failed", "MA rejected the call.");
        Assert.Null(await new MusicWidgetData(_player, _speakers).GetAsync(Request("""{"speaker":"kitchen"}"""), Ct));
    }

    [Fact]
    public async Task QueueAndPlay_DeclareTheMusicWidget()
    {
        IReadOnlyList<ITool> tools = await new MusicToolSource(_player, _speakers, Options.Create(new HomeAssistantOptions { Url = new Uri("http://ha") })).GetToolsAsync(Ct);

        Assert.Equal(["play", "queue"], tools.Where(tool => tool.Descriptor.Widget == "music").Select(tool => tool.Descriptor.Id.Name).Order(StringComparer.Ordinal));
    }

    private static WidgetRequest Request(string arguments) =>
        new(new ToolId("music", "queue"), JsonDocument.Parse(arguments).RootElement.Clone(), new ToolOutput("{}"), TestExecutionContext.Text());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;
}
