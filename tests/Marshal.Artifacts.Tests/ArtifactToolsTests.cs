using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
using Marshal.Abstractions.Displays;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Testing.TestExecutionContext;

namespace Marshal.Artifacts.Tests;

public sealed class ArtifactToolsTests : IAsyncLifetime
{
    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-artifact-tools-").FullName;
    private static readonly byte[] _png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, 1, 2, 3, 4];

    private readonly RecordingPlayer _player = new();
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 10, 8, 10, 0, 0, TimeSpan.Zero));
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = Text(OwnerA);
    }

    public async ValueTask DisposeAsync()
    {
        await _database.DisposeAsync();
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task List_UntrustedWithArtifactTaint()
    {
        ToolDescriptor list = (await ToolAsync("list")).Descriptor;

        Assert.Equal((true, "artifact", RiskTier.Safe), (list.UntrustedOutput, list.TaintKind, list.DefaultTier));
        Assert.False((await ToolAsync("show")).Descriptor.UntrustedOutput);
    }

    [Fact]
    public async Task List_FiltersAndLimitsToFifty()
    {
        for (int i = 0; i < 55; i++)
        {
            await CreateAsync($"note{i}.md", "x");
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        await CreateAsync("bracket.scad", "cube(1);");

        JsonElement all = await ListAsync(new { });
        JsonElement code = await ListAsync(new { kind = "code" });
        JsonElement named = await ListAsync(new { query = "note54" });

        Assert.Equal(50, all.GetArrayLength());
        Assert.Equal("bracket.scad", all[0].GetProperty("name").GetString());
        Assert.Equal(("bracket.scad", "code", "upload"), (Assert.Single(code.EnumerateArray()).GetProperty("name").GetString(), code[0].GetProperty("kind").GetString(), code[0].GetProperty("source").GetString()));
        Assert.Equal("note54.md", Assert.Single(named.EnumerateArray()).GetProperty("name").GetString());
        Assert.Equal(ErrorType.Validation, (await InvokeAsync("list", new { kind = "sculpture" }, Text(OwnerA))).Error.Type);
    }

    [Fact]
    public async Task Show_OwnedId_ReturnsTheRef()
    {
        ArtifactRecord model = await CreateAsync("bracket.stl", "solid b\n facet normal 0 0 1\n endfacet\nendsolid");

        Result<ToolOutput> shown = await InvokeAsync("show", new { id = model.Id.ToString() }, Web());

        ArtifactRef reference = Assert.Single(shown.Value.Artifacts!);
        Assert.Equal(new ArtifactRef(model.Id, "bracket.stl", ArtifactKind.Model3d, $"/content/{model.Id}"), reference);
    }

    [Fact]
    public async Task Show_ForeignOrUnknownId_NotFound()
    {
        ArtifactRecord mine = await CreateAsync("a.md", "a");
        _database.Accessor.Current = Text(OwnerB);

        Assert.Equal(ErrorType.NotFound, (await InvokeAsync("show", new { id = mine.Id.ToString() }, Web(OwnerB))).Error.Type);
        Assert.Equal(ErrorType.NotFound, (await InvokeAsync("show", new { id = "nope" }, Web(OwnerB))).Error.Type);
    }

    [Fact]
    public async Task Show_VisibleInWebTurnsAndOnSatellitesWithDisplay()
    {
        ITool show = await ToolAsync("show");

        Assert.True(await show.IsAvailableAsync(Web(), Ct));
        Assert.True(await show.IsAvailableAsync(LivingRoom, Ct));
        Assert.False(await show.IsAvailableAsync(Voice(OwnerA, satelliteId: "assist_satellite.kitchen"), Ct));
        Assert.False(await show.IsAvailableAsync(Voice(OwnerA), Ct));
        Assert.False(await show.IsAvailableAsync(Text(OwnerA) with { Source = SessionSource.Task }, Ct));
        Assert.True(await (await ToolAsync("list")).IsAvailableAsync(Voice(OwnerA), Ct));
    }

    [Fact]
    public async Task Show_SatelliteWithDisplayButNoHomeAssistant_Hidden()
    {
        ITool show = (await new ArtifactToolSource(Store(), new FakeLinks(), Displays(withPlayer: false)).GetToolsAsync(Ct)).Single(tool => tool.Descriptor.Id.Name == "show");

        Assert.False(await show.IsAvailableAsync(LivingRoom, Ct));
    }

    [Fact]
    public async Task Show_OnSatellite_PlaysFirstPageOnItsDisplay()
    {
        ArtifactRecord photo = await CreateAsync("photo.png", _png);

        Result<ToolOutput> shown = await InvokeAsync("show", new { id = photo.Id.ToString() }, LivingRoom);

        Assert.Equal("Showing photo.png on the display living_tv, page 1 of 1.", shown.Value.Content);
        Assert.Null(shown.Value.Artifacts);
        (DisplaySurface display, string url, DisplayMediaType type) = Assert.Single(_player.Played);
        Assert.Equal((new DisplaySurface("living_tv", "media_player.living_room_tv"), $"http://tv.test/{photo.Id}/1.png", DisplayMediaType.Image), (display, url, type));
    }

    [Fact]
    public async Task Show_OnSatellite_SecondPageOfCode()
    {
        ArtifactRecord code = await CreateAsync("Program.cs", string.Concat(Enumerable.Range(1, 130).Select(line => $"line {line}\n")));

        Result<ToolOutput> shown = await InvokeAsync("show", new { id = code.Id.ToString(), page = 2 }, LivingRoom);

        Assert.Equal("Showing Program.cs on the display living_tv, page 2 of 4.", shown.Value.Content);
        Assert.Equal($"http://tv.test/{code.Id}/2.png", Assert.Single(_player.Played).Url);
    }

    [Fact]
    public async Task Show_OnSatellite_PageOutOfRange_Invalid()
    {
        ArtifactRecord photo = await CreateAsync("photo.png", _png);

        Result<ToolOutput> shown = await InvokeAsync("show", new { id = photo.Id.ToString(), page = 2 }, LivingRoom);

        Assert.Equal("artifacts.invalid_arguments", shown.Error.Code);
        Assert.Empty(_player.Played);
    }

    [Fact]
    public async Task Show_OnSatellite_ModelHasNoRendition()
    {
        ArtifactRecord model = await CreateAsync("bracket.stl", "solid b\n facet normal 0 0 1\n endfacet\nendsolid");

        Result<ToolOutput> shown = await InvokeAsync("show", new { id = model.Id.ToString() }, LivingRoom);

        Assert.Equal("artifacts.no_rendition", shown.Error.Code);
        Assert.Empty(_player.Played);
    }

    [Fact]
    public async Task Show_OnSatellite_UnknownId_NotFound()
    {
        Assert.Equal(ErrorType.NotFound, (await InvokeAsync("show", new { id = ArtifactId.New().ToString() }, LivingRoom)).Error.Type);
        Assert.Empty(_player.Played);
    }

    [Fact]
    public async Task Show_OnSatellite_PlayerFails_ErrorReturned()
    {
        ArtifactRecord photo = await CreateAsync("photo.png", _png);
        _player.Failure = new Error("display.failed", "The display 'living_tv' did not accept the media.", ErrorType.Unavailable);

        Assert.Equal("display.failed", (await InvokeAsync("show", new { id = photo.Id.ToString() }, LivingRoom)).Error.Code);
    }

    private static MarshalExecutionContext LivingRoom => Voice(OwnerA, satelliteId: "assist_satellite.living_room");

    private static MarshalExecutionContext Web(Abstractions.Identity.OwnerId? owner = null) => Text(owner ?? OwnerA) with { Source = SessionSource.Web };

    private async Task<JsonElement> ListAsync(object arguments) =>
        JsonDocument.Parse((await InvokeAsync("list", arguments, Text(OwnerA))).Value.Content).RootElement.Clone();

    private async Task<Result<ToolOutput>> InvokeAsync(string name, object arguments, MarshalExecutionContext context) =>
        await (await ToolAsync(name)).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), context), Ct);

    private async Task<ITool> ToolAsync(string name) =>
        (await new ArtifactToolSource(Store(), new FakeLinks(), Displays(withPlayer: true)).GetToolsAsync(Ct)).Single(tool => tool.Descriptor.Id.Name == name);

    private ArtifactDisplays Displays(bool withPlayer) =>
        new(new SkiaArtifactRenditions(Store()), new FakeDisplayLinks(), new FakeDisplayDirectory(), withPlayer ? [_player] : []);

    private Task<ArtifactRecord> CreateAsync(string name, string content) => CreateAsync(name, Encoding.UTF8.GetBytes(content));

    private async Task<ArtifactRecord> CreateAsync(string name, byte[] content) =>
        (await Store().CreateAsync(new ArtifactDraft(name, ArtifactSource.Upload), new MemoryStream(content), Ct)).Value;

    private FileArtifactStore Store() =>
        new(_database.Factory, _database.Accessor, new ArtifactKindDetector(), new ArtifactDirectory(_directory), Options.Create(new ArtifactOptions()), new FakeLiveEventPublisher(), _time);

    private sealed class FakeDisplayLinks : IDisplayLinks
    {
        public string Create(ArtifactId id, int page, string extension) => $"http://tv.test/{id}/{page}.{extension}";
    }

    private sealed class FakeDisplayDirectory : IDisplayDirectory
    {
        public DisplaySurface? ForSatellite(string? satelliteId) =>
            satelliteId == "assist_satellite.living_room" ? new DisplaySurface("living_tv", "media_player.living_room_tv") : null;
    }

    private sealed class RecordingPlayer : IDisplayPlayer
    {
        public bool IsAvailable => true;

        public List<(DisplaySurface Display, string Url, DisplayMediaType Type)> Played { get; } = [];

        public Error? Failure { get; set; }

        public Task<Result> PlayAsync(DisplaySurface display, string url, DisplayMediaType type, CancellationToken cancellationToken)
        {
            if (Failure is { } failure)
                return Task.FromResult(Result.Failure(failure));

            Played.Add((display, url, type));
            return Task.FromResult(Result.Success());
        }
    }

    private sealed class FakeLinks : IArtifactLinks
    {
        public string Content(ArtifactId id) => $"/content/{id}";

        public string SignedContent(ArtifactId id) => $"https://marshal.test/signed/{id}";
    }
}
