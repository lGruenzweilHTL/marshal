using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Music;
using Marshal.Integrations.HomeAssistant.Music;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Marshal.Integrations.HomeAssistant.Tests.Music;

public sealed class HomeAssistantMusicPlayerTests
{
    private static readonly MusicSpeaker _kitchen = new("kitchen", "media_player.kitchen_ma");
    private static readonly MusicSpeaker _living = new("living_room", "media_player.living_room_sonos");
    private static readonly MusicSpeaker _office = new("office", "media_player.office_ma");

    private readonly FakeMusicCommands _commands = new();
    private readonly FixtureCache _cache = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private HomeAssistantMusicPlayer Player => new(_commands, _cache);

    [Fact]
    public async Task Play_Defaults_SendsPlayMediaWithItemAsMediaId()
    {
        Result played = await Player.PlayAsync(_kitchen, new MusicPlayRequest("Queen - Innuendo", null, MusicEnqueue.Play, false), Ct);

        Assert.True(played.IsSuccess);
        JsonObject call = Assert.Single(_commands.ServiceCalls);
        AssertCall(call, "music_assistant", "play_media", _kitchen.Entity);
        Assert.Equal("Queen - Innuendo", (string?)call["service_data"]!["media_id"]);
        Assert.Equal("play", (string?)call["service_data"]!["enqueue"]);
        Assert.False(call["service_data"]!.AsObject().ContainsKey("media_type"));
        Assert.False(call["service_data"]!.AsObject().ContainsKey("radio_mode"));
        Assert.False(call.ContainsKey("return_response"));
    }

    [Theory]
    [InlineData(MusicEnqueue.Replace, "replace")]
    [InlineData(MusicEnqueue.Next, "next")]
    [InlineData(MusicEnqueue.ReplaceNext, "replace_next")]
    [InlineData(MusicEnqueue.Add, "add")]
    public async Task Play_EnqueueTypeAndRadio_AreSent(MusicEnqueue enqueue, string expected)
    {
        await Player.PlayAsync(_living, new MusicPlayRequest("library://album/12", MusicMediaType.Album, enqueue, true), Ct);

        JsonObject call = Assert.Single(_commands.ServiceCalls);
        AssertCall(call, "music_assistant", "play_media", _living.Entity);
        Assert.Equal(("library://album/12", "album", expected, true), (
            (string?)call["service_data"]!["media_id"],
            (string?)call["service_data"]!["media_type"],
            (string?)call["service_data"]!["enqueue"],
            (bool?)call["service_data"]!["radio_mode"]));
    }

    [Theory]
    [InlineData(MusicAction.Pause, "media_pause")]
    [InlineData(MusicAction.Resume, "media_play")]
    [InlineData(MusicAction.Next, "media_next_track")]
    [InlineData(MusicAction.Previous, "media_previous_track")]
    [InlineData(MusicAction.Stop, "media_stop")]
    public async Task Control_Action_SendsMediaPlayerService(MusicAction action, string service)
    {
        Result controlled = await Player.ControlAsync(_kitchen, action, null, Ct);

        Assert.True(controlled.IsSuccess);
        JsonObject call = Assert.Single(_commands.ServiceCalls);
        AssertCall(call, "media_player", service, _kitchen.Entity);
        Assert.Empty(call["service_data"]!.AsObject());
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(35, 0.35)]
    [InlineData(100, 1.0)]
    public async Task Control_Volume_ScalesToVolumeLevel(int volume, double level)
    {
        await Player.ControlAsync(_kitchen, MusicAction.Volume, volume, Ct);

        JsonObject call = Assert.Single(_commands.ServiceCalls);
        AssertCall(call, "media_player", "volume_set", _kitchen.Entity);
        Assert.Equal(level, (double)call["service_data"]!["volume_level"]!, 3);
    }

    [Fact]
    public async Task Transfer_TargetsDestinationWithSourcePlayer()
    {
        Result transferred = await Player.TransferAsync(_kitchen, _living, Ct);

        Assert.True(transferred.IsSuccess);
        JsonObject call = Assert.Single(_commands.ServiceCalls);
        AssertCall(call, "music_assistant", "transfer_queue", _living.Entity);
        Assert.Equal(_kitchen.Entity, (string?)call["service_data"]!["source_player"]);
        Assert.True((bool?)call["service_data"]!["auto_play"]);
    }

    [Fact]
    public async Task Group_JoinsMembersToTheLeader()
    {
        Result grouped = await Player.GroupAsync(_kitchen, [_living, _office], Ct);

        Assert.True(grouped.IsSuccess);
        JsonObject call = Assert.Single(_commands.ServiceCalls);
        AssertCall(call, "media_player", "join", _kitchen.Entity);
        Assert.Equal([_living.Entity, _office.Entity], call["service_data"]!["group_members"]!.AsArray().Select(member => (string)member!));
    }

    [Fact]
    public async Task Ungroup_SendsUnjoin()
    {
        await Player.UngroupAsync(_office, Ct);

        AssertCall(Assert.Single(_commands.ServiceCalls), "media_player", "unjoin", _office.Entity);
    }

    [Fact]
    public async Task Search_ReturnsItemsPerTypeWithArtistsAlbumAndUri()
    {
        Result<IReadOnlyList<MusicItem>> found = await Player.SearchAsync("Innuendo", null, 10, Ct);

        Assert.True(found.IsSuccess, found.IsFailure ? found.Error.Message : null);
        JsonObject call = Assert.Single(_commands.ServiceCalls);
        Assert.Equal(("music_assistant", "search", true), ((string?)call["domain"], (string?)call["service"], (bool?)call["return_response"]));
        Assert.False(call.ContainsKey("target"));
        Assert.Equal(("01J9MUSICASSISTANT0000000", "Innuendo", 10), (
            (string?)call["service_data"]!["config_entry_id"],
            (string?)call["service_data"]!["name"],
            (int?)call["service_data"]!["limit"]));
        Assert.False(call["service_data"]!.AsObject().ContainsKey("media_type"));

        Assert.Equal(6, found.Value.Count);
        MusicItem album = found.Value.Single(item => item.Uri == "library://album/12");
        Assert.Equal((MusicMediaType.Album, "Innuendo", null), (album.Type, album.Name, album.Album));
        Assert.Equal(["Queen"], album.Artists);
        MusicItem track = found.Value.Single(item => item.Uri == "tidal://track/4489");
        Assert.Equal((MusicMediaType.Track, "Under Pressure", null), (track.Type, track.Name, track.Album));
        Assert.Equal(["Queen", "David Bowie"], track.Artists);
        Assert.Equal("Innuendo", found.Value.Single(item => item.Uri == "library://track/101").Album);
        Assert.Equal(MusicMediaType.Radio, found.Value.Single(item => item.Uri == "tunein://radio/s24896").Type);
    }

    [Fact]
    public async Task Search_WithType_SendsMediaTypeList()
    {
        await Player.SearchAsync("Innuendo", MusicMediaType.Album, 5, Ct);

        JsonObject call = Assert.Single(_commands.ServiceCalls);
        Assert.Equal(["album"], call["service_data"]!["media_type"]!.AsArray().Select(type => (string)type!));
    }

    [Fact]
    public async Task Search_ConfigEntryIdIsCachedAndReloadedAfterAFailedCall()
    {
        await Player.SearchAsync("a", null, 10, Ct);
        HomeAssistantMusicPlayer player = Player;
        await player.SearchAsync("b", null, 10, Ct);
        await player.SearchAsync("c", null, 10, Ct);
        Assert.Equal(2, _commands.ConfigEntryReads);

        _commands.FailingServices["music_assistant/search"] = "Config entry not loaded";
        Result<IReadOnlyList<MusicItem>> failed = await player.SearchAsync("d", null, 10, Ct);
        _commands.FailingServices.Clear();
        await player.SearchAsync("e", null, 10, Ct);

        Assert.Equal("music.failed", failed.Error.Code);
        Assert.Equal(3, _commands.ConfigEntryReads);
    }

    [Fact]
    public async Task Search_PrefersALoadedEntry()
    {
        _commands.ConfigEntries = JsonDocument.Parse("""
            [{"entry_id":"old","domain":"music_assistant","state":"setup_error"},{"entry_id":"new","domain":"music_assistant","state":"loaded"}]
            """).RootElement.Clone();

        await Player.SearchAsync("a", null, 10, Ct);

        Assert.Equal("new", (string?)Assert.Single(_commands.ServiceCalls)["service_data"]!["config_entry_id"]);
    }

    [Fact]
    public async Task Search_WithoutMusicAssistantEntry_FailsWithMusicFailed()
    {
        _commands.ConfigEntries = JsonDocument.Parse("[]").RootElement.Clone();

        Result<IReadOnlyList<MusicItem>> found = await Player.SearchAsync("a", null, 10, Ct);

        Assert.Equal("music.failed", found.Error.Code);
        Assert.Contains("Music Assistant", found.Error.Message, StringComparison.Ordinal);
        Assert.Empty(_commands.ServiceCalls);
    }

    [Fact]
    public async Task Queue_ReturnsStateCurrentNextCountShuffleAndRepeat()
    {
        _cache.Current = FixtureCache.Recorded with
        {
            States = ImmutableDictionary<string, HaState>.Empty.Add(_kitchen.Entity, new HaState(_kitchen.Entity, "playing", default, null)),
        };

        Result<MusicQueueState> queue = await Player.GetQueueAsync(_kitchen, Ct);

        Assert.True(queue.IsSuccess, queue.IsFailure ? queue.Error.Message : null);
        JsonObject call = Assert.Single(_commands.ServiceCalls);
        AssertCall(call, "music_assistant", "get_queue", _kitchen.Entity);
        Assert.True((bool?)call["return_response"]);
        Assert.Equal(("playing", 12, false, "off"), (queue.Value.State, queue.Value.Items, queue.Value.Shuffle, queue.Value.Repeat));
        Assert.Equal((MusicMediaType.Track, "Innuendo", "Innuendo", "library://track/101"), (queue.Value.Current!.Type, queue.Value.Current.Name, queue.Value.Current.Album, queue.Value.Current.Uri));
        Assert.Equal(["Queen"], queue.Value.Current.Artists);
        Assert.Equal("I'm Going Slightly Mad", queue.Value.Next!.Name);
    }

    [Fact]
    public async Task Queue_EmptyQueue_HasNoItems()
    {
        _commands.Queue = JsonDocument.Parse("""
            {"context":{},"response":{"media_player.kitchen_ma":{"queue_id":"kitchen_ma","active":false,"items":0,"shuffle_enabled":false,"repeat_mode":"off","current_item":null,"next_item":null}}}
            """).RootElement.Clone();

        Result<MusicQueueState> queue = await Player.GetQueueAsync(_kitchen, Ct);

        Assert.True(queue.IsSuccess);
        Assert.Equal((0, null, null), (queue.Value.Items, queue.Value.Current, queue.Value.Next));
    }

    [Fact]
    public async Task HaRejectsTheCall_FailsWithMusicFailedNamingTheSpeaker()
    {
        _commands.FailingServices["music_assistant/play_media"] = "Player is unavailable";

        Result played = await Player.PlayAsync(_kitchen, new MusicPlayRequest("x", null, MusicEnqueue.Play, false), Ct);

        Assert.Equal("music.failed", played.Error.Code);
        Assert.Contains("kitchen", played.Error.Message, StringComparison.Ordinal);
        Assert.Contains("Player is unavailable", played.Error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Disconnected_FailsWithHaUnreachable()
    {
        _commands.Connected = false;

        Result controlled = await Player.ControlAsync(_kitchen, MusicAction.Pause, null, Ct);
        Result<IReadOnlyList<MusicItem>> found = await Player.SearchAsync("a", null, 10, Ct);

        Assert.Equal(("ha.unreachable", ErrorType.Unavailable), (controlled.Error.Code, controlled.Error.Type));
        Assert.Equal("ha.unreachable", found.Error.Code);
    }

    [Fact]
    public async Task Wire_FakeHomeAssistant_ReceivesCallServiceAndAnswersSearch()
    {
        await using FakeHomeAssistant ha = await FakeHomeAssistant.StartAsync();
        ha.Responses["config_entries/get"] = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "music-config-entries.json")));
        ha.ServiceResponses["music_assistant/search"] = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "music-search.json")));
        IOptions<HomeAssistantOptions> options = Options.Create(new HomeAssistantOptions { Url = new Uri("http://homeassistant.local:8123"), Token = FakeHomeAssistant.Token });
        using HomeAssistantCache cache = new(options, new TestServerSockets(ha.Server), new HomeAssistantCacheTimings { CommandTimeout = TimeSpan.FromSeconds(2) }, TimeProvider.System, NullLogger<HomeAssistantCache>.Instance);
        await cache.StartAsync(Ct);
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (cache.Current is null && DateTime.UtcNow < deadline)
                await Task.Delay(10, Ct);
            var player = new HomeAssistantMusicPlayer(cache, cache);

            Result<IReadOnlyList<MusicItem>> found = await player.SearchAsync("Innuendo", null, 10, Ct);
            Result played = await player.PlayAsync(_kitchen, new MusicPlayRequest("library://album/12", MusicMediaType.Album, MusicEnqueue.Add, false), Ct);

            Assert.True(found.IsSuccess, found.IsFailure ? found.Error.Message : null);
            Assert.Contains(found.Value, item => item.Uri == "library://album/12");
            Assert.True(played.IsSuccess, played.IsFailure ? played.Error.Message : null);
            JsonObject play = ha.Commands.Last(command => (string?)command["type"] == "call_service");
            AssertCall(play, "music_assistant", "play_media", _kitchen.Entity);
            Assert.Equal("add", (string?)play["service_data"]!["enqueue"]);
        }
        finally
        {
            await cache.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Wire_FailingService_FailsWithMusicFailed()
    {
        await using FakeHomeAssistant ha = await FakeHomeAssistant.StartAsync();
        ha.FailingCommands["call_service"] = "Entity media_player.kitchen_ma is unavailable";
        IOptions<HomeAssistantOptions> options = Options.Create(new HomeAssistantOptions { Url = new Uri("http://homeassistant.local:8123"), Token = FakeHomeAssistant.Token });
        using HomeAssistantCache cache = new(options, new TestServerSockets(ha.Server), new HomeAssistantCacheTimings { CommandTimeout = TimeSpan.FromSeconds(2) }, TimeProvider.System, NullLogger<HomeAssistantCache>.Instance);
        await cache.StartAsync(Ct);
        try
        {
            DateTime deadline = DateTime.UtcNow.AddSeconds(10);
            while (cache.Current is null && DateTime.UtcNow < deadline)
                await Task.Delay(10, Ct);

            Result paused = await new HomeAssistantMusicPlayer(cache, cache).ControlAsync(_kitchen, MusicAction.Pause, null, Ct);

            Assert.Equal("music.failed", paused.Error.Code);
            Assert.Contains("kitchen", paused.Error.Message, StringComparison.Ordinal);
        }
        finally
        {
            await cache.StopAsync(CancellationToken.None);
        }
    }

    private static void AssertCall(JsonObject call, string domain, string service, string entity)
    {
        Assert.Equal((domain, service), ((string?)call["domain"], (string?)call["service"]));
        Assert.Equal(entity, (string?)call["target"]!["entity_id"]);
    }

    private static JsonElement Fixture(string file) => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", file))).RootElement.Clone();

    private sealed class FakeMusicCommands : IHomeAssistantCommands
    {
        public JsonElement ConfigEntries { get; set; } = Fixture("music-config-entries.json");

        public JsonElement Search { get; set; } = Fixture("music-search.json");

        public JsonElement Queue { get; set; } = Fixture("music-queue.json");

        public bool Connected { get; set; } = true;

        /// <summary><c>domain/service</c> → HA error message.</summary>
        public Dictionary<string, string> FailingServices { get; } = new(StringComparer.Ordinal);

        public List<JsonObject> ServiceCalls { get; } = [];

        public int ConfigEntryReads { get; private set; }

        public Task<Result<JsonElement>> CommandAsync(string type, JsonObject fields, CancellationToken cancellationToken)
        {
            if (!Connected)
                return Task.FromResult<Result<JsonElement>>(new Error("ha.unavailable", "Home Assistant is not connected.", ErrorType.Unavailable));

            Result<JsonElement> result;
            if (type == "config_entries/get")
            {
                ConfigEntryReads++;
                Assert.Equal("music_assistant", (string?)fields["domain"]);
                result = ConfigEntries;
            }
            else if (type == "call_service")
            {
                ServiceCalls.Add((JsonObject)fields.DeepClone());
                string key = $"{fields["domain"]}/{fields["service"]}";
                result = FailingServices.TryGetValue(key, out string? failure)
                    ? new Error("ha.command_failed", failure, ErrorType.Unavailable)
                    : key switch
                    {
                        "music_assistant/search" => Search,
                        "music_assistant/get_queue" => Queue,
                        _ => JsonDocument.Parse("""{"context":{}}""").RootElement.Clone(),
                    };
            }
            else
            {
                result = new Error("ha.command_failed", $"Unknown command {type}", ErrorType.Unavailable);
            }

            return Task.FromResult(result);
        }
    }
}
