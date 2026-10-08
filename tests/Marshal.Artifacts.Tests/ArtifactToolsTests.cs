using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Artifacts;
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
    public async Task Show_VisibleOnlyInWebTurns()
    {
        ITool show = await ToolAsync("show");

        Assert.True(await show.IsAvailableAsync(Web(), Ct));
        Assert.False(await show.IsAvailableAsync(Voice(OwnerA), Ct));
        Assert.False(await show.IsAvailableAsync(Text(OwnerA) with { Source = SessionSource.Task }, Ct));
        Assert.True(await (await ToolAsync("list")).IsAvailableAsync(Voice(OwnerA), Ct));
    }

    private static MarshalExecutionContext Web(Abstractions.Identity.OwnerId? owner = null) => Text(owner ?? OwnerA) with { Source = SessionSource.Web };

    private async Task<JsonElement> ListAsync(object arguments) =>
        JsonDocument.Parse((await InvokeAsync("list", arguments, Text(OwnerA))).Value.Content).RootElement.Clone();

    private async Task<Result<ToolOutput>> InvokeAsync(string name, object arguments, MarshalExecutionContext context) =>
        await (await ToolAsync(name)).InvokeAsync(new ToolInvocation(JsonSerializer.SerializeToElement(arguments), context), Ct);

    private async Task<ITool> ToolAsync(string name) =>
        (await new ArtifactToolSource(Store(), new FakeLinks()).GetToolsAsync(Ct)).Single(tool => tool.Descriptor.Id.Name == name);

    private async Task<ArtifactRecord> CreateAsync(string name, string content) =>
        (await Store().CreateAsync(new ArtifactDraft(name, ArtifactSource.Upload), new MemoryStream(Encoding.UTF8.GetBytes(content)), Ct)).Value;

    private FileArtifactStore Store() =>
        new(_database.Factory, _database.Accessor, new ArtifactKindDetector(), new ArtifactDirectory(_directory), Options.Create(new ArtifactOptions()), new FakeLiveEventPublisher(), _time);

    private sealed class FakeLinks : IArtifactLinks
    {
        public string Content(ArtifactId id) => $"/content/{id}";

        public string SignedContent(ArtifactId id) => $"https://marshal.test/signed/{id}";
    }
}
