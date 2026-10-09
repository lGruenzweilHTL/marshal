using System.Net;
using System.Net.Http.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Displays;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.Tokens;
using Marshal.Api.Tests.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Marshal.Api.Tests.Displays;

public sealed class DisplayTurnTests
{
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3, 4];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task VoiceTurn_ShowsArtifactOnTheSatellitesDisplay_AndTheLinkServesIt()
    {
        var player = new RecordingPlayer();
        using var factory = new MarshalApiFactory(new()
        {
            ["displays:mediaBaseUrl"] = "http://192.168.1.10:8080/",
            ["displays:surfaces:living_tv:entity"] = "media_player.living_room_tv",
        })
        {
            TestServices = services => services.AddSingleton<IDisplayPlayer>(player),
        };
        await File.WriteAllTextAsync(Path.Combine(factory.ConfigDirectory, "satellites.yaml"), "satellites:\n  assist_satellite.living_room:\n    display: living_tv\n", Ct);
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);
        ArtifactId id = await CreateArtifactAsync(factory, "photo.png", _png);
        factory.Model.ThenToolCall("artifacts_show", new { id = id.ToString() }).ThenText("It is on the TV.");

        HttpResponseMessage response = await client.PostAsJsonAsync("/api/v1/conversation/turns", new
        {
            conversationId = "c1",
            text = "show me the photo",
            language = "en",
            satelliteId = "assist_satellite.living_room",
            inputMode = "voice",
        }, Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        (DisplaySurface display, string url, DisplayMediaType type) = Assert.Single(player.Played);
        Assert.Equal(("media_player.living_room_tv", DisplayMediaType.Image), (display.Entity, type));
        Assert.StartsWith($"http://192.168.1.10:8080/api/v1/artifacts/{id}/display/1/", url, StringComparison.Ordinal);
        HttpResponseMessage media = await factory.CreateClient().GetAsync(new Uri(url).PathAndQuery, Ct);
        Assert.Equal(HttpStatusCode.OK, media.StatusCode);
        Assert.Equal(_png, await media.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task VoiceTurn_SatelliteWithoutDisplay_ShowIsNotOffered()
    {
        using var factory = new MarshalApiFactory(new()
        {
            ["displays:mediaBaseUrl"] = "http://192.168.1.10:8080/",
            ["displays:surfaces:living_tv:entity"] = "media_player.living_room_tv",
        })
        {
            TestServices = services => services.AddSingleton<IDisplayPlayer>(new RecordingPlayer()),
        };
        HttpClient client = await factory.ClientWithTokenAsync(ApiScopes.ConversationWrite);
        factory.Model.ThenText("I cannot show things here.");

        await client.PostAsJsonAsync("/api/v1/conversation/turns", new { conversationId = "c1", text = "show me the photo", satelliteId = "assist_satellite.kitchen", inputMode = "voice" }, Ct);

        Assert.DoesNotContain(Assert.Single(factory.Model.Requests).Options!.Tools!, tool => tool.Name == "artifacts_show");
    }

    private static async Task<ArtifactId> CreateArtifactAsync(MarshalApiFactory factory, string name, byte[] content)
    {
        _ = factory.Server;
        OwnerId owner = await factory.Services.GetRequiredService<IInstanceOwner>().GetAsync(Ct);
        using IDisposable scope = factory.Services.GetRequiredService<IExecutionContextAccessor>().Use(new MarshalExecutionContext { OwnerId = owner, Source = SessionSource.Web });
        Result<ArtifactRecord> created = await factory.Services.GetRequiredService<IArtifactStore>().CreateAsync(new ArtifactDraft(name, ArtifactSource.Upload), new MemoryStream(content), Ct);
        return created.Value.Id;
    }

    private sealed class RecordingPlayer : IDisplayPlayer
    {
        public bool IsAvailable => true;

        public List<(DisplaySurface Display, string Url, DisplayMediaType Type)> Played { get; } = [];

        public Task<Result> PlayAsync(DisplaySurface display, string url, DisplayMediaType type, CancellationToken cancellationToken)
        {
            Played.Add((display, url, type));
            return Task.FromResult(Result.Success());
        }
    }
}
