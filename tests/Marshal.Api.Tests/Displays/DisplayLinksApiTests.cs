using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Identity;
using Marshal.Api.Tests.Auth;
using Marshal.Api.Tests.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Api.Tests.Displays;

public sealed class DisplayLinksApiTests : IAsyncLifetime
{
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3, 4];
    private static readonly byte[] _mp4 = [0, 0, 0, 0x18, .. "ftypmp42"u8, 0, 0, 0, 0, .. "mp42isom"u8];

    private readonly FakeTimeProvider _time = new(DateTimeOffset.UtcNow);
    private MarshalApiFactory _factory = null!;
    private HttpClient _browser = null!;
    private OwnerId _user;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        Dictionary<string, string?> settings = AuthFactory.Local().ToSettings(0);
        settings["displays:mediaBaseUrl"] = "http://192.168.1.10:8080/";
        _factory = new MarshalApiFactory(settings) { TestServices = services => services.AddSingleton<TimeProvider>(_time) };
        (_browser, _user) = await Browser.SignInAsync(_factory, "user");
    }

    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    [Fact]
    public async Task Link_UnderMediaBaseUrlWithExtension()
    {
        string id = await UploadIdAsync("photo.png", _png);

        string link = Link(id, 1, "png");

        Assert.StartsWith($"http://192.168.1.10:8080/api/v1/artifacts/{id}/display/1/", link, StringComparison.Ordinal);
        Assert.EndsWith(".png", link, StringComparison.Ordinal);
        Assert.DoesNotContain("?", link, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Link_ServesRenditionWithoutSignIn()
    {
        string id = await UploadIdAsync("photo.png", _png);

        HttpResponseMessage response = await Anonymous().GetAsync(Path(Link(id, 1, "png")), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));
        Assert.Equal(_png, await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task Link_RangeAndHead()
    {
        string id = await UploadIdAsync("clip.mp4", _mp4);
        string path = Path(Link(id, 1, "mp4"));
        _time.Advance(TimeSpan.FromMinutes(90));

        using var range = new HttpRequestMessage(HttpMethod.Get, path);
        range.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(4, 7);
        HttpResponseMessage partial = await Anonymous().SendAsync(range, Ct);
        HttpResponseMessage head = await Anonymous().SendAsync(new HttpRequestMessage(HttpMethod.Head, path), Ct);

        Assert.Equal(HttpStatusCode.PartialContent, partial.StatusCode);
        Assert.Equal("ftyp"u8.ToArray(), await partial.Content.ReadAsByteArrayAsync(Ct));
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Equal("video/mp4", head.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Link_Expired_NotFound()
    {
        string id = await UploadIdAsync("photo.png", _png);
        string path = Path(Link(id, 1, "png"));

        _time.Advance(TimeSpan.FromMinutes(241));

        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync(path, Ct)).StatusCode);
    }

    [Fact]
    public async Task Link_AlteredPageIdOrExtension_NotFound()
    {
        string first = await UploadIdAsync("one.png", _png);
        string second = await UploadIdAsync("two.png", _png);
        string path = Path(Link(first, 1, "png"));

        HttpResponseMessage page = await Anonymous().GetAsync(path.Replace("/display/1/", "/display/2/", StringComparison.Ordinal), Ct);
        HttpResponseMessage foreign = await Anonymous().GetAsync(path.Replace(first, second, StringComparison.Ordinal), Ct);
        HttpResponseMessage extension = await Anonymous().GetAsync(path[..^4] + ".jpg", Ct);
        HttpResponseMessage signature = await Anonymous().GetAsync(path[..^7] + "abc.png", Ct);

        Assert.Equal(
            (HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound),
            (page.StatusCode, foreign.StatusCode, extension.StatusCode, signature.StatusCode));
    }

    [Fact]
    public async Task BrowserSignature_IsNoDisplaySignature()
    {
        string id = await UploadIdAsync("photo.png", _png);
        string signed;
        IExecutionContextAccessor accessor = _factory.Services.GetRequiredService<IExecutionContextAccessor>();
        using (accessor.Use(new MarshalExecutionContext { OwnerId = _user, Source = SessionSource.Web }))
            signed = _factory.Services.GetRequiredService<IArtifactLinks>().SignedContent(ArtifactId.Parse(id, null));
        string signature = Uri.UnescapeDataString(signed[(signed.IndexOf("signature=", StringComparison.Ordinal) + "signature=".Length)..]);

        HttpResponseMessage response = await Anonymous().GetAsync($"/api/v1/artifacts/{id}/display/1/{signature}.png", Ct);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Link_DeletedArtifact_NotFound()
    {
        string id = await UploadIdAsync("photo.png", _png);
        string path = Path(Link(id, 1, "png"));

        Assert.Equal(HttpStatusCode.NoContent, (await _browser.DeleteAsync($"/api/v1/artifacts/{id}", Ct)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await Anonymous().GetAsync(path, Ct)).StatusCode);
    }

    [Fact]
    public async Task Link_CodePage_RenderedPng()
    {
        string id = await UploadIdAsync("Program.cs", "class Program { }\n"u8.ToArray());

        HttpResponseMessage response = await Anonymous().GetAsync(Path(Link(id, 1, "png")), Ct);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("image/png", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(_png[..4], (await response.Content.ReadAsByteArrayAsync(Ct))[..4]);
    }

    private HttpClient Anonymous() => _factory.CreateClient();

    private string Link(string id, int page, string extension)
    {
        IExecutionContextAccessor accessor = _factory.Services.GetRequiredService<IExecutionContextAccessor>();
        using (accessor.Use(new MarshalExecutionContext { OwnerId = _user, Source = SessionSource.Web }))
            return _factory.Services.GetRequiredService<IDisplayLinks>().Create(ArtifactId.Parse(id, null), page, extension);
    }

    // The test server answers on its own origin; the media base URL's origin only matters to the TV.
    private static string Path(string link) => new Uri(link).PathAndQuery;

    private async Task<string> UploadIdAsync(string name, byte[] content)
    {
        using var form = new MultipartFormDataContent { { new ByteArrayContent(content), "file", name } };
        HttpResponseMessage response = await _browser.PostAsync("/api/v1/artifacts", form, Ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(Ct)).GetProperty("id").GetString()!;
    }
}
