using System.Text.Json.Nodes;
using Marshal.Abstractions;
using Marshal.Abstractions.Printers;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Integrations.Moonraker.Tests;

public sealed class MoonrakerSnapshotSourceTests : IAsyncLifetime
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "marshal-snapshots-" + Guid.NewGuid().ToString("N"));
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 29, 10, 15, 0, TimeSpan.Zero));
    private FakeMoonraker _fake = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _fake = await FakeMoonraker.StartAsync();
        _fake.Rest["GET /server/webcams/list"] = JsonNode.Parse("""
            {"webcams":[
              {"name":"off","enabled":false,"service":"mjpegstreamer","snapshot_url":"/off/?action=snapshot","stream_url":"/off/?action=stream"},
              {"name":"main","enabled":true,"service":"mjpegstreamer-adaptive","snapshot_url":"/webcam/?action=snapshot","stream_url":"/webcam/?action=stream","flip_horizontal":false,"flip_vertical":false,"rotation":0}]}
            """);
        _fake.Raw["/webcam/"] = ("image/jpeg", TestImages.Create(2000, 1500));
    }

    public async ValueTask DisposeAsync()
    {
        await _fake.DisposeAsync();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task Webcams_FirstEnabledWithUrlsResolvedAgainstThePrinter()
    {
        PrinterWebcam webcam = (await Webcams().FindAsync("voron", Ct)).Value;

        Assert.Equal("main", webcam.Name);
        Assert.Equal(new Uri(_fake.Server.BaseAddress, "/webcam/?action=snapshot"), webcam.SnapshotUrl);
        Assert.Equal(new Uri(_fake.Server.BaseAddress, "/webcam/?action=stream"), webcam.StreamUrl);
        Assert.Equal(FakeMoonraker.ApiKey, _fake.RestRequests.Single(request => request.Path == "/server/webcams/list").ApiKey);
    }

    [Fact]
    public async Task Webcams_AbsoluteUrlsKept()
    {
        _fake.Rest["GET /server/webcams/list"] = JsonNode.Parse("""
            {"webcams":[{"name":"cam","enabled":true,"snapshot_url":"http://camera.lan/snap.jpg","stream_url":""}]}
            """);

        PrinterWebcam webcam = (await Webcams().FindAsync("voron", Ct)).Value;

        Assert.Equal((new Uri("http://camera.lan/snap.jpg"), (Uri?)null), (webcam.SnapshotUrl, webcam.StreamUrl));
    }

    [Fact]
    public async Task Webcams_NoneEnabled_NoWebcam()
    {
        _fake.Rest["GET /server/webcams/list"] = JsonNode.Parse("""{"webcams":[]}""");

        Assert.Equal("printer.no_webcam", (await Webcams().FindAsync("voron", Ct)).Error.Code);
    }

    [Fact]
    public async Task Stream_ProxiesTheStreamUrl()
    {
        _fake.Raw["/webcam/"] = ("multipart/x-mixed-replace;boundary=frame", "--frame\r\n"u8.ToArray());

        Result<WebcamFeed> stream = await Webcams().OpenStreamAsync("voron", Ct);

        Assert.Equal("multipart/x-mixed-replace; boundary=frame", stream.Value.ContentType);
        using var reader = new StreamReader(stream.Value.Body);
        Assert.Equal("--frame\r\n", await reader.ReadToEndAsync(Ct));
        await stream.Value.DisposeAsync();
    }

    [Fact]
    public async Task Stream_WebcamWithoutStream_NoWebcam()
    {
        _fake.Rest["GET /server/webcams/list"] = JsonNode.Parse("""
            {"webcams":[{"name":"cam","enabled":true,"snapshot_url":"/snap.jpg","stream_url":""}]}
            """);

        Assert.Equal("printer.no_webcam", (await Webcams().OpenStreamAsync("voron", Ct)).Error.Code);
    }

    [Fact]
    public async Task Capture_ResizedStoredAndLinkedSameOrigin()
    {
        PrinterSnapshot snapshot = (await Source().CaptureAsync("voron", Ct)).Value;

        Assert.Equal((1024, 768), (snapshot.Width, snapshot.Height));
        Assert.True(TestImages.IsJpeg(snapshot.Jpeg));
        Assert.Equal($"/app/api/snapshots/voron/{snapshot.Id}", snapshot.Url);
        Assert.Equal(_time.GetUtcNow(), snapshot.CapturedAt);
        string file = Path.Combine(_directory, "voron", snapshot.Id + ".jpg");
        Assert.Equal(snapshot.Jpeg, await File.ReadAllBytesAsync(file, Ct));
        Assert.Equal(snapshot.Jpeg, (await Source().OpenAsync("voron", snapshot.Id, Ct)).Value);
    }

    [Fact]
    public async Task Capture_KeepsOnlyTheNewestSnapshots()
    {
        MoonrakerSnapshotSource source = Source(retention: 2);
        var ids = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            ids.Add((await source.CaptureAsync("voron", Ct)).Value.Id);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        Assert.Equal(ids.Skip(1).Order(StringComparer.Ordinal), Directory.GetFiles(Path.Combine(_directory, "voron")).Select(Path.GetFileNameWithoutExtension).Order(StringComparer.Ordinal));
        Assert.Equal("printer.snapshot_not_found", (await source.OpenAsync("voron", ids[0], Ct)).Error.Code);
    }

    [Fact]
    public async Task Capture_WebcamFails_SnapshotFailed()
    {
        _fake.Raw.Clear();
        _fake.RestErrors["GET /webcam/"] = (System.Net.HttpStatusCode.ServiceUnavailable, "no camera");

        Assert.Equal("printer.snapshot_failed", (await Source().CaptureAsync("voron", Ct)).Error.Code);
    }

    [Fact]
    public async Task Capture_NotAnImage_SnapshotFailed()
    {
        _fake.Raw["/webcam/"] = ("text/html", "<html>login</html>"u8.ToArray());

        Assert.Equal("printer.snapshot_failed", (await Source().CaptureAsync("voron", Ct)).Error.Code);
    }

    [Theory]
    [InlineData("voron", "../../etc/passwd")]
    [InlineData("voron", "missing")]
    [InlineData("prusa", "20260929T101500000")]
    public async Task Open_UnknownOrUnsafe_NotFound(string printer, string id)
    {
        Result<byte[]> result = await Source().OpenAsync(printer, id, Ct);

        Assert.True(result.IsFailure);
        Assert.Contains(result.Error.Code, new[] { "printer.snapshot_not_found", "printer.not_found" });
    }

    private MoonrakerWebcams Webcams() => new(Options(), new SingleClientFactory(_fake.Server.CreateClient()));

    private MoonrakerSnapshotSource Source(int retention = 20) => new(
        Options(retention),
        Webcams(),
        new SingleClientFactory(_fake.Server.CreateClient()),
        new SkiaImageResizer(),
        new PrefixLinks(),
        new SnapshotStorage(_directory),
        _time);

    private Microsoft.Extensions.Options.IOptions<PrinterOptions> Options(int retention = 20) =>
        Microsoft.Extensions.Options.Options.Create(new PrinterOptions
        {
            Printers = [new PrinterConfig { Name = "voron", Url = _fake.Server.BaseAddress, ApiKey = FakeMoonraker.ApiKey }],
            PrinterSnapshots = new SnapshotOptions { MaxSize = 1024, Quality = 80, Retention = retention },
        });

    private sealed class PrefixLinks : ISnapshotLinks
    {
        public string For(string printer, string id) => $"/app/api/snapshots/{printer}/{id}";
    }
}
