using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Abstractions.LiveEvents;
using Marshal.Api.Artifacts;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Api.Tests.Artifacts;

public sealed class ArtifactsApiTests : IAsyncLifetime
{
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3, 4];

    private readonly FakeLiveEventPublisher _live = new();
    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["artifacts:maxUploadMb"] = "1";
        _factory = new MarshalApiFactory(settings)
        {
            TestServices = services =>
            {
                services.AddSingleton<ILiveEventPublisher>(_live);
                services.AddSingleton<TimeProvider>(_time);
            },
        };
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Upload_CreatesUntaintedArtifactAndPublishesIt()
    {
        HttpResponseMessage response = await UploadAsync("notes.md", "# Notes\n"u8.ToArray());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        JsonElement created = await response.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal(("notes.md", "markdown", "upload", false), (created.GetProperty("name").GetString(), created.GetProperty("kind").GetString(), created.GetProperty("source").GetString(), created.GetProperty("tainted").GetBoolean()));
        LiveEvent published = Assert.Single(_live.Events, entry => entry.Topic == "artifacts");
        Assert.Equal(("artifact.created", _user), (published.Type, published.Owner));
        Assert.Contains(_factory.Services.GetServices<LiveTopic>(), topic => topic.Name == "artifacts");
        JsonElement list = await _browser.GetFromJsonAsync<JsonElement>("/api/v1/artifacts", Ct);
        Assert.Equal("notes.md", Assert.Single(list.GetProperty("items").EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(8, list.GetProperty("usedBytes").GetInt64());
    }

    [Fact]
    public async Task Upload_OverTheLimit_413AndNothingStored()
    {
        HttpResponseMessage response = await UploadAsync("big.bin", new byte[(1024 * 1024) + 1]);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Empty((await _browser.GetFromJsonAsync<JsonElement>("/api/v1/artifacts", Ct)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task OverQuota_ProblemNamingTheQuota()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["artifacts:quotaMb"] = "1";
        await using var factory = new MarshalApiFactory(settings);
        (HttpClient browser, _) = await Browser.SignInAsync(factory, "user");
        await UploadAsync(browser, "one.bin", new byte[700 * 1024]);

        HttpResponseMessage second = await UploadAsync(browser, "two.bin", new byte[700 * 1024]);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        JsonElement problem = await second.Content.ReadFromJsonAsync<JsonElement>(Ct);
        Assert.Equal("artifacts.quota_exceeded", problem.GetProperty("code").GetString());
        Assert.Contains("1 MB", problem.GetProperty("detail").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task OtherOwnersArtifact_NotFoundEverywhere()
    {
        string id = await UploadIdAsync("a.md", "a"u8.ToArray());
        (HttpClient other, _) = await Browser.SignInAsync(_factory, "user");

        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/artifacts/{id}", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/v1/artifacts/{id}/content", Ct)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.DeleteAsync($"/api/v1/artifacts/{id}", Ct)).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<JsonElement>("/api/v1/artifacts", Ct)).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task Delete_RemovesItAndPublishes()
    {
        string id = await UploadIdAsync("a.md", "a"u8.ToArray());

        HttpResponseMessage deleted = await _browser.DeleteAsync($"/api/v1/artifacts/{id}", Ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _browser.GetAsync($"/api/v1/artifacts/{id}/content", Ct)).StatusCode);
        Assert.Equal("artifact.deleted", _live.Events.Last().Type);
        Assert.True(await _factory.WithDatabaseAsync(_user, db => db.AuditEvents.AnyAsync(entry => entry.Action == "artifact.delete" && entry.Target == id, Ct)));
    }

    [Theory]
    [InlineData("photo.png", "image/png", "inline")]
    [InlineData("notes.md", "text/markdown; charset=utf-8", "inline")]
    [InlineData("mock.html", "text/html; charset=utf-8", "inline")]
    [InlineData("icon.svg", "image/svg+xml", "inline")]
    [InlineData("data.bin", "application/octet-stream", "attachment")]
    public async Task Content_NosniffDetectedTypeAndDisposition(string name, string mediaType, string disposition)
    {
        byte[] content = name switch
        {
            "photo.png" => _png,
            "icon.svg" => "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray(),
            "mock.html" => "<p>hi</p><script>alert(1)</script>"u8.ToArray(),
            _ => "x"u8.ToArray(),
        };
        string id = await UploadIdAsync(name, content);

        HttpResponseMessage response = await _browser.GetAsync($"/api/v1/artifacts/{id}/content", Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(mediaType, response.Content.Headers.ContentType!.ToString());
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal(disposition, response.Content.Headers.ContentDisposition!.DispositionType);
        string policy = response.Headers.GetValues("Content-Security-Policy").Single();
        if (name is "mock.html" or "icon.svg")
        {
            Assert.Equal(ArtifactEndpoints.SandboxPolicy, policy);
            Assert.DoesNotContain("allow-scripts", policy, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("sandbox", policy, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task Content_DisguisedHtmlAsPng_IsADownload()
    {
        string id = await UploadIdAsync("photo.png", "<html><script>alert(1)</script></html>"u8.ToArray());

        HttpResponseMessage response = await _browser.GetAsync($"/api/v1/artifacts/{id}/content", Ct);

        Assert.Equal(("application/octet-stream", "attachment"), (response.Content.Headers.ContentType!.MediaType, response.Content.Headers.ContentDisposition!.DispositionType));
    }

    [Fact]
    public async Task Content_VideoRange_PartialContent()
    {
        byte[] mp4 = [0, 0, 0, 0x18, (byte)'f', (byte)'t', (byte)'y', (byte)'p', (byte)'i', (byte)'s', (byte)'o', (byte)'m', 9, 9, 9, 9];
        string id = await UploadIdAsync("clip.mp4", mp4);
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/artifacts/{id}/content");
        request.Headers.Range = new RangeHeaderValue(4, 7);

        HttpResponseMessage response = await _browser.SendAsync(request, Ct);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal("ftyp"u8.ToArray(), await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task SignedLink_WorksWithoutSessionUntilItExpires()
    {
        string id = await UploadIdAsync("photo.png", _png);
        string link = SignedLink(id);
        HttpClient anonymous = _factory.CreateClient();

        _time.Advance(TimeSpan.FromMinutes(10));
        HttpResponseMessage valid = await anonymous.GetAsync(link, Ct);
        _time.Advance(TimeSpan.FromMinutes(6));
        HttpResponseMessage expired = await anonymous.GetAsync(link, Ct);

        Assert.Equal(HttpStatusCode.OK, valid.StatusCode);
        Assert.Equal(_png, await valid.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.NotFound, expired.StatusCode);
    }

    [Fact]
    public async Task SignedLink_AlteredOrForAnotherArtifact_NotFound()
    {
        string first = await UploadIdAsync("one.png", _png);
        string second = await UploadIdAsync("two.png", _png);
        string link = SignedLink(first);
        HttpClient anonymous = _factory.CreateClient();

        HttpResponseMessage altered = await anonymous.GetAsync(link[..^3] + "abc", Ct);
        HttpResponseMessage foreign = await anonymous.GetAsync(link.Replace(first, second, StringComparison.Ordinal), Ct);
        HttpResponseMessage unsigned = await anonymous.GetAsync($"/api/v1/artifacts/{first}/content", Ct);

        Assert.Equal((HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.Unauthorized), (altered.StatusCode, foreign.StatusCode, unsigned.StatusCode));
    }

    private string SignedLink(string id)
    {
        IExecutionContextAccessor accessor = _factory.Services.GetRequiredService<IExecutionContextAccessor>();
        using (accessor.Use(new MarshalExecutionContext { OwnerId = _user, Source = SessionSource.Web }))
            return _factory.Services.GetRequiredService<IArtifactLinks>().SignedContent(ArtifactId.Parse(id, null));
    }

    private async Task<string> UploadIdAsync(string name, byte[] content)
    {
        HttpResponseMessage response = await UploadAsync(name, content);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }

    private Task<HttpResponseMessage> UploadAsync(string name, byte[] content) => UploadAsync(_browser, name, content);

    private static async Task<HttpResponseMessage> UploadAsync(HttpClient client, string name, byte[] content)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", name } };
        return await client.PostAsync("/api/v1/artifacts", form, Ct);
    }
}
