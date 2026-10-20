using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Music;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.HomeAssistant.Music;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Integrations.HomeAssistant.Tests.Music;

public sealed class MusicToolsTests
{
    private readonly FakeMusicPlayer _player = new();
    private readonly FakeSpeakerDirectory _speakers = new(("kitchen", "media_player.kitchen_ma"), ("living_room", "media_player.living_room_sonos"), ("office", "media_player.office_ma"));

    public MusicToolsTests()
    {
        _speakers.Satellites["assist_satellite.kitchen"] = "kitchen";
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Tools_SevenSafeToolsInTheMusicNamespace_SearchAndQueueUntrusted()
    {
        IReadOnlyList<ITool> tools = await Source().GetToolsAsync(Ct);

        Assert.Equal(["control", "group", "play", "queue", "search", "transfer", "ungroup"], tools.Select(tool => tool.Descriptor.Id.Name).Order(StringComparer.Ordinal));
        Assert.All(tools, tool => Assert.Equal(("music", RiskTier.Safe, false), (tool.Descriptor.Id.Namespace, tool.Descriptor.DefaultTier, tool.Descriptor.Persists)));
        Assert.Equal(["queue", "search"], tools.Where(tool => tool.Descriptor.UntrustedOutput).Select(tool => tool.Descriptor.Id.Name).Order(StringComparer.Ordinal));
        Assert.All(tools.Where(tool => tool.Descriptor.UntrustedOutput), tool => Assert.Equal("music", tool.Descriptor.TaintKind));
    }

    [Fact]
    public async Task Tools_WithoutHomeAssistantOrSpeakers_None()
    {
        Assert.Empty(await new MusicToolSource(_player, _speakers, Options.Create(new HomeAssistantOptions())).GetToolsAsync(Ct));
        Assert.Empty(await Source(new FakeSpeakerDirectory()).GetToolsAsync(Ct));
    }

    [Fact]
    public async Task Play_WithoutSpeakerFromASatellite_PlaysOnTheProfileSpeaker()
    {
        Result<ToolOutput> played = await InvokeAsync("play", new { item = "Queen - Innuendo" }, Voice());

        Assert.True(played.IsSuccess, played.IsFailure ? played.Error.Message : null);
        (MusicSpeaker speaker, MusicPlayRequest request) = Assert.Single(_player.Plays);
        Assert.Equal("kitchen", speaker.Name);
        Assert.Equal(new MusicPlayRequest("Queen - Innuendo", null, MusicEnqueue.Play, false), request);
        Assert.Contains("kitchen", played.Value.Content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Play_NamedSpeaker_PlaysThere()
    {
        await InvokeAsync("play", new { item = "library://album/12", type = "album", speaker = "living_room", enqueue = "add", radio = true }, Voice());

        (MusicSpeaker speaker, MusicPlayRequest request) = Assert.Single(_player.Plays);
        Assert.Equal("media_player.living_room_sonos", speaker.Entity);
        Assert.Equal(new MusicPlayRequest("library://album/12", MusicMediaType.Album, MusicEnqueue.Add, true), request);
    }

    [Fact]
    public async Task Play_WebTurnWithoutSpeaker_NoSpeakerListingNames()
    {
        Result<ToolOutput> played = await InvokeAsync("play", new { item = "Innuendo" }, Text());

        Assert.Equal("music.no_speaker", played.Error.Code);
        Assert.Contains("kitchen, living_room, office", played.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_player.Plays);
    }

    [Fact]
    public async Task Play_SatelliteWithoutProfileSpeaker_NoSpeaker()
    {
        Result<ToolOutput> played = await InvokeAsync("play", new { item = "Innuendo" }, Voice(satelliteId: "assist_satellite.garage"));

        Assert.Equal("music.no_speaker", played.Error.Code);
    }

    [Theory]
    [InlineData("bathroom")]
    [InlineData("media_player.kitchen_ma")]
    public async Task Play_UnknownSpeakerOrEntityId_UnknownSpeakerListingNames(string speaker)
    {
        Result<ToolOutput> played = await InvokeAsync("play", new { item = "Innuendo", speaker }, Voice());

        Assert.Equal("music.unknown_speaker", played.Error.Code);
        Assert.Contains(speaker, played.Error.Message, StringComparison.Ordinal);
        Assert.Contains("kitchen, living_room, office", played.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_player.Plays);
    }

    [Theory]
    [InlineData("https://example.com/stream.mp3")]
    [InlineData("http://track/x")]
    [InlineData("HTTPS://example.com/a")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://example.com/a.mp3")]
    [InlineData("rtsp://camera.local/stream")]
    [InlineData("file://track/1")]
    [InlineData("custom://example.com/x")]
    [InlineData("spotify://playlist/")]
    public async Task Play_Url_InvalidArgumentsAndNothingSent(string item)
    {
        ITool play = await ToolAsync("play");

        Result validated = play.Validate(Arguments(new { item }));
        Result<ToolOutput> played = await InvokeAsync("play", new { item }, Voice());

        Assert.Equal("music.invalid_arguments", validated.Error.Code);
        Assert.Equal("music.invalid_arguments", played.Error.Code);
        Assert.Empty(_player.Plays);
    }

    [Theory]
    [InlineData("library://album/12")]
    [InlineData("spotify://playlist/37i9dQZF1DXcBWIGoYBM5M")]
    [InlineData("tunein://radio/s24896")]
    [InlineData("Queen - Innuendo")]
    public async Task Play_MusicAssistantUriOrPhrase_Accepted(string item)
    {
        ITool play = await ToolAsync("play");

        Assert.True(play.Validate(Arguments(new { item })).IsSuccess);
        await InvokeAsync("play", new { item }, Voice());

        Assert.Equal(item, Assert.Single(_player.Plays).Request.Item);
    }

    [Theory]
    [InlineData("""{"item":""}""")]
    [InlineData("""{}""")]
    [InlineData("""{"item":"x","enqueue":"later"}""")]
    [InlineData("""{"item":"x","type":"podcast"}""")]
    [InlineData("""{"item":"x","radio":"yes"}""")]
    public async Task Play_BadArguments_Invalid(string arguments)
    {
        Assert.Equal("music.invalid_arguments", (await ToolAsync("play")).Validate(JsonDocument.Parse(arguments).RootElement).Error.Code);
    }

    [Fact]
    public async Task Search_GroupsUpToTenItemsPerTypeWithArtistsAlbumAndUri()
    {
        _player.Found =
        [
            .. Enumerable.Range(1, 12).Select(number => new MusicItem(MusicMediaType.Track, $"Track {number}", ["Queen"], "Innuendo", $"library://track/{number}")),
            new MusicItem(MusicMediaType.Album, "Innuendo", ["Queen"], null, "library://album/12"),
        ];

        JsonElement found = await JsonAsync("search", new { query = "Innuendo" }, Text());

        Assert.Equal(("Innuendo", null, 10), Assert.Single(_player.Searches));
        Assert.Equal(10, found.GetProperty("tracks").GetArrayLength());
        JsonElement track = found.GetProperty("tracks")[0];
        Assert.Equal(("Track 1", "Queen", "Innuendo", "library://track/1"), (
            track.GetProperty("name").GetString(),
            track.GetProperty("artists")[0].GetString(),
            track.GetProperty("album").GetString(),
            track.GetProperty("uri").GetString()));
        Assert.Equal("library://album/12", found.GetProperty("albums")[0].GetProperty("uri").GetString());
        Assert.False(found.TryGetProperty("playlists", out _));
    }

    [Fact]
    public async Task Search_WithType_PassesIt()
    {
        await InvokeAsync("search", new { query = "Queen", type = "radio" }, Text());

        Assert.Equal(("Queen", MusicMediaType.Radio, 10), Assert.Single(_player.Searches));
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"query":" "}""")]
    [InlineData("""{"query":"x","type":"movie"}""")]
    public async Task Search_BadArguments_Invalid(string arguments)
    {
        Assert.Equal("music.invalid_arguments", (await ToolAsync("search")).Validate(JsonDocument.Parse(arguments).RootElement).Error.Code);
    }

    [Fact]
    public async Task Queue_ShowsSpeakerStateCurrentNextCountShuffleAndRepeat()
    {
        _player.Queue = new MusicQueueState(
            "playing",
            new MusicItem(MusicMediaType.Track, "Innuendo", ["Queen"], "Innuendo", "library://track/101"),
            new MusicItem(MusicMediaType.Track, "I'm Going Slightly Mad", ["Queen"], "Innuendo", "library://track/102"),
            12,
            false,
            "off");

        JsonElement queue = await JsonAsync("queue", new { }, Voice());

        Assert.Equal("kitchen", Assert.Single(_player.QueueReads).Name);
        Assert.Equal(("kitchen", "playing", 12, false, "off"), (
            queue.GetProperty("speaker").GetString(),
            queue.GetProperty("state").GetString(),
            queue.GetProperty("items").GetInt32(),
            queue.GetProperty("shuffle").GetBoolean(),
            queue.GetProperty("repeat").GetString()));
        Assert.Equal(("Innuendo", "library://track/101"), (queue.GetProperty("current").GetProperty("name").GetString(), queue.GetProperty("current").GetProperty("uri").GetString()));
        Assert.Equal("Queen", queue.GetProperty("current").GetProperty("artists")[0].GetString());
        Assert.Equal("I'm Going Slightly Mad", queue.GetProperty("next").GetProperty("name").GetString());
    }

    [Theory]
    [InlineData("pause", MusicAction.Pause)]
    [InlineData("resume", MusicAction.Resume)]
    [InlineData("next", MusicAction.Next)]
    [InlineData("previous", MusicAction.Previous)]
    [InlineData("stop", MusicAction.Stop)]
    public async Task Control_Action_SentToTheSpeaker(string action, MusicAction expected)
    {
        Result<ToolOutput> controlled = await InvokeAsync("control", new { action, speaker = "office" }, Voice());

        Assert.True(controlled.IsSuccess);
        Assert.Equal(("office", expected, (int?)null), Assert.Single(_player.Controls));
    }

    [Fact]
    public async Task Control_Volume_SentWithTheValue()
    {
        await InvokeAsync("control", new { action = "volume", volume = 35 }, Voice());

        Assert.Equal(("kitchen", MusicAction.Volume, (int?)35), Assert.Single(_player.Controls));
    }

    [Theory]
    [InlineData("""{"action":"volume","volume":150}""")]
    [InlineData("""{"action":"volume","volume":-1}""")]
    [InlineData("""{"action":"volume","volume":12.5}""")]
    [InlineData("""{"action":"volume"}""")]
    [InlineData("""{"action":"louder"}""")]
    [InlineData("""{}""")]
    public async Task Control_BadArguments_InvalidAndNothingSent(string arguments)
    {
        ITool control = await ToolAsync("control");

        Result validated = control.Validate(JsonDocument.Parse(arguments).RootElement);
        Result<ToolOutput> controlled = await control.InvokeAsync(new ToolInvocation(JsonDocument.Parse(arguments).RootElement, Voice()), Ct);

        Assert.Equal("music.invalid_arguments", validated.Error.Code);
        Assert.Equal("music.invalid_arguments", controlled.Error.Code);
        Assert.Empty(_player.Controls);
    }

    [Fact]
    public async Task Transfer_FromTheTurnsSpeaker()
    {
        Result<ToolOutput> transferred = await InvokeAsync("transfer", new { to = "living_room" }, Voice());

        Assert.True(transferred.IsSuccess);
        Assert.Equal(("kitchen", "living_room"), Assert.Single(_player.Transfers));
    }

    [Fact]
    public async Task Transfer_ExplicitFrom()
    {
        await InvokeAsync("transfer", new { to = "kitchen", from = "office" }, Text());

        Assert.Equal(("office", "kitchen"), Assert.Single(_player.Transfers));
    }

    [Fact]
    public async Task Transfer_WebTurnWithoutFrom_NoSpeaker()
    {
        Assert.Equal("music.no_speaker", (await InvokeAsync("transfer", new { to = "kitchen" }, Text())).Error.Code);
    }

    [Fact]
    public async Task Transfer_ToItself_Invalid()
    {
        Assert.Equal("music.invalid_arguments", (await InvokeAsync("transfer", new { to = "kitchen" }, Voice())).Error.Code);
        Assert.Empty(_player.Transfers);
    }

    [Fact]
    public async Task Group_FirstSpeakerLeads()
    {
        Result<ToolOutput> grouped = await InvokeAsync("group", new { speakers = new[] { "living_room", "kitchen", "office" } }, Text());

        Assert.True(grouped.IsSuccess);
        (MusicSpeaker leader, IReadOnlyList<MusicSpeaker> members) = Assert.Single(_player.Groups);
        Assert.Equal("living_room", leader.Name);
        Assert.Equal(["kitchen", "office"], members.Select(member => member.Name));
    }

    [Theory]
    [InlineData("""{"speakers":["kitchen"]}""")]
    [InlineData("""{"speakers":["kitchen","kitchen"]}""")]
    [InlineData("""{"speakers":[]}""")]
    [InlineData("""{"speakers":"kitchen"}""")]
    [InlineData("""{}""")]
    public async Task Group_LessThanTwoDistinctSpeakers_Invalid(string arguments)
    {
        Assert.Equal("music.invalid_arguments", (await ToolAsync("group")).Validate(JsonDocument.Parse(arguments).RootElement).Error.Code);
    }

    [Fact]
    public async Task Group_UnknownSpeaker_UnknownSpeakerAndNothingSent()
    {
        Assert.Equal("music.unknown_speaker", (await InvokeAsync("group", new { speakers = new[] { "kitchen", "bathroom" } }, Text())).Error.Code);
        Assert.Empty(_player.Groups);
    }

    [Fact]
    public async Task Ungroup_RemovesTheSpeaker()
    {
        await InvokeAsync("ungroup", new { speaker = "office" }, Voice());

        Assert.Equal("office", Assert.Single(_player.Ungroups).Name);
    }

    [Fact]
    public async Task PlayerFailure_ReturnedAsIs()
    {
        _player.Failure = new Error("ha.unreachable", "Home Assistant is not reachable.", ErrorType.Unavailable);

        Assert.Equal("ha.unreachable", (await InvokeAsync("control", new { action = "pause" }, Voice())).Error.Code);
        Assert.Equal("ha.unreachable", (await InvokeAsync("search", new { query = "x" }, Voice())).Error.Code);
    }

    private MusicToolSource Source(ISpeakerDirectory? speakers = null) =>
        new(_player, speakers ?? _speakers, Options.Create(new HomeAssistantOptions { Url = new Uri("http://ha.local/") }));

    private async Task<ITool> ToolAsync(string name) =>
        (await Source().GetToolsAsync(Ct)).Single(tool => tool.Descriptor.Id.Name == name);

    private async Task<Result<ToolOutput>> InvokeAsync(string name, object arguments, MarshalExecutionContext context) =>
        await (await ToolAsync(name)).InvokeAsync(new ToolInvocation(Arguments(arguments), context), Ct);

    private async Task<JsonElement> JsonAsync(string name, object arguments, MarshalExecutionContext context)
    {
        Result<ToolOutput> output = await InvokeAsync(name, arguments, context);
        Assert.True(output.IsSuccess, output.IsFailure ? output.Error.Message : null);
        return JsonDocument.Parse(output.Value.Content).RootElement.Clone();
    }

    private static JsonElement Arguments(object arguments) => JsonSerializer.SerializeToElement(arguments);

    internal sealed class FakeMusicPlayer : IMusicPlayer
    {
        public IReadOnlyList<MusicItem> Found { get; set; } = [];

        public MusicQueueState Queue { get; set; } = new(null, null, null, 0, false, null);

        public Error? Failure { get; set; }

        public List<(string Query, MusicMediaType? Type, int Limit)> Searches { get; } = [];

        public List<(MusicSpeaker Speaker, MusicPlayRequest Request)> Plays { get; } = [];

        public List<MusicSpeaker> QueueReads { get; } = [];

        public List<(string Speaker, MusicAction Action, int? Volume)> Controls { get; } = [];

        public List<(string From, string To)> Transfers { get; } = [];

        public List<(MusicSpeaker Leader, IReadOnlyList<MusicSpeaker> Members)> Groups { get; } = [];

        public List<MusicSpeaker> Ungroups { get; } = [];

        public Task<Result<IReadOnlyList<MusicItem>>> SearchAsync(string query, MusicMediaType? type, int limit, CancellationToken cancellationToken)
        {
            Searches.Add((query, type, limit));
            return Task.FromResult(Failure is { } failure ? (Result<IReadOnlyList<MusicItem>>)failure : Result.Success(Found));
        }

        public Task<Result> PlayAsync(MusicSpeaker speaker, MusicPlayRequest request, CancellationToken cancellationToken)
        {
            Plays.Add((speaker, request));
            return Done();
        }

        public Task<Result<MusicQueueState>> GetQueueAsync(MusicSpeaker speaker, CancellationToken cancellationToken)
        {
            QueueReads.Add(speaker);
            return Task.FromResult(Failure is { } failure ? (Result<MusicQueueState>)failure : Queue);
        }

        public Task<Result> ControlAsync(MusicSpeaker speaker, MusicAction action, int? volume, CancellationToken cancellationToken)
        {
            Controls.Add((speaker.Name, action, volume));
            return Done();
        }

        public Task<Result> TransferAsync(MusicSpeaker from, MusicSpeaker to, CancellationToken cancellationToken)
        {
            Transfers.Add((from.Name, to.Name));
            return Done();
        }

        public Task<Result> GroupAsync(MusicSpeaker leader, IReadOnlyList<MusicSpeaker> members, CancellationToken cancellationToken)
        {
            Groups.Add((leader, members));
            return Done();
        }

        public Task<Result> UngroupAsync(MusicSpeaker speaker, CancellationToken cancellationToken)
        {
            Ungroups.Add(speaker);
            return Done();
        }

        private Task<Result> Done() => Task.FromResult(Failure is { } failure ? Result.Failure(failure) : Result.Success());
    }
}
