using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Music;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Integrations.HomeAssistant.Music;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marshal.Api.Tests.Widgets;

public sealed class MusicWidgetApiTests : IAsyncLifetime
{
    private readonly RecordingPlayer _player = new();
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["ha:url"] = "http://127.0.0.1:1";
        settings["ha:tokenSecret"] = "ha_token";
        settings["music:speakers:kitchen"] = "media_player.kitchen_ma";
        _factory = new MarshalApiFactory(settings)
        {
            TestServices = services =>
            {
                services.RemoveAll<IMusicPlayer>();
                services.AddSingleton<IMusicPlayer>(_player);
            },
        };
        await File.WriteAllTextAsync(Path.Combine(_factory.SecretsDirectory, "ha_token"), "token\n", Ct);
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Queue_WidgetData()
    {
        _player.Queue = new MusicQueueState("playing", new MusicItem(MusicMediaType.Track, "Innuendo", ["Queen"], null, null), null, 3, false, null);

        JsonElement queue = await _browser.GetFromJsonAsync<JsonElement>("/api/v1/music/kitchen", Ct);

        Assert.Equal(("kitchen", "Innuendo"), (queue.GetProperty("speaker").GetString(), queue.GetProperty("current").GetProperty("name").GetString()));
    }

    [Fact]
    public async Task Control_RunsTheToolThroughTheGuard_RecordedAsWidgetRun()
    {
        HttpResponseMessage response = await _browser.PostAsJsonAsync("/api/v1/music/kitchen/control", new { action = "volume", volume = 30 }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(("kitchen", MusicAction.Volume, 30), Assert.Single(_player.Controls));
        Marshal.Data.Conversations.Run run = await _factory.WithDatabaseAsync(_user, db => db.Runs.SingleAsync(Ct));
        Assert.Equal((SessionSource.Web, "widget:music"), (run.Source, run.Trigger));
        Marshal.Data.Conversations.RunStep step = await _factory.WithDatabaseAsync(_user, db => db.RunSteps.SingleAsync(Ct));
        Assert.Equal("music.control", step.Tool);
    }

    [Fact]
    public async Task Control_InvalidAction_RejectedByTheTool()
    {
        HttpResponseMessage response = await _browser.PostAsJsonAsync("/api/v1/music/kitchen/control", new { action = "shuffle_all_the_things" }, Ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(_player.Controls);
    }

    [Fact]
    public async Task UnknownSpeaker_404AndTokensRejected()
    {
        HttpResponseMessage unknown = await _browser.PostAsJsonAsync("/api/v1/music/garage/control", new { action = "pause" }, Ct);
        using HttpClient machine = await _factory.ClientWithTokenAsync(Marshal.Abstractions.Tokens.ApiScopes.ConversationWrite);
        HttpResponseMessage token = await machine.PostAsJsonAsync("/api/v1/music/kitchen/control", new { action = "pause" }, Ct);

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, token.StatusCode);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private sealed class RecordingPlayer : IMusicPlayer
    {
        public MusicQueueState Queue { get; set; } = new(null, null, null, 0, false, null);

        public List<(string Speaker, MusicAction Action, int? Volume)> Controls { get; } = [];

        public Task<Result<IReadOnlyList<MusicItem>>> SearchAsync(string query, MusicMediaType? type, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<MusicItem>>([]));

        public Task<Result> PlayAsync(MusicSpeaker speaker, MusicPlayRequest request, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result<MusicQueueState>> GetQueueAsync(MusicSpeaker speaker, CancellationToken cancellationToken) => Task.FromResult(Result.Success(Queue));

        public Task<Result> ControlAsync(MusicSpeaker speaker, MusicAction action, int? volume, CancellationToken cancellationToken)
        {
            Controls.Add((speaker.Name, action, volume));
            return Task.FromResult(Result.Success());
        }

        public Task<Result> TransferAsync(MusicSpeaker from, MusicSpeaker to, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result> GroupAsync(MusicSpeaker leader, IReadOnlyList<MusicSpeaker> members, CancellationToken cancellationToken) => Task.FromResult(Result.Success());

        public Task<Result> UngroupAsync(MusicSpeaker speaker, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }
}
