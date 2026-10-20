using System.Text;
using System.Text.Json;
using Marshal.Abstractions;
using Marshal.Abstractions.Memory;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Marshal.Memory.Tests;

public sealed class NdjsonMemoryExporterTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private TestDatabase _database = null!;
    private SqliteMemoryStore _store = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _store = MemoryTestKit.Store(_database, _embeddings, _time);
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Export_OneObjectPerLineStableOrderWithoutEmbeddings()
    {
        await SeedAsync();

        string[] lines = (await ExportAsync(_database)).Split('\n', StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["project", "chunk", "chunk", "chunk", "version", "edge", "edge"], lines.Select(line => JsonDocument.Parse(line).RootElement.GetProperty("kind").GetString()));
        Assert.All(lines, line => Assert.StartsWith("{\"kind\":", line, StringComparison.Ordinal));
        Assert.DoesNotContain(lines, line => line.Contains("embedding", StringComparison.OrdinalIgnoreCase));
        string[] chunkFields = [.. JsonDocument.Parse(lines[1]).RootElement.EnumerateObject().Select(property => property.Name)];
        Assert.Equal(["kind", "id", "scope", "projectId", "type", "title", "content", "importance", "pinned", "sourceRef", "createdBy", "createdAt", "updatedAt", "deletedAt", "validFrom", "validTo"], chunkFields);
    }

    [Fact]
    public async Task Export_ChunksAndVersionsCarryValidTime()
    {
        await SeedAsync();

        JsonElement[] lines = [.. (await ExportAsync(_database)).Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement)];

        JsonElement superseded = lines.Single(line => line.GetProperty("kind").GetString() == "chunk" && line.GetProperty("title").GetString() == "Voron nozzle size");
        Assert.Equal(("2026-09-28T08:59:59.0000000+00:00", "2026-09-28T09:00:00.0000000+00:00"), (superseded.GetProperty("validFrom").GetString(), superseded.GetProperty("validTo").GetString()));
        JsonElement version = lines.Single(line => line.GetProperty("kind").GetString() == "version");
        Assert.Equal(("2026-09-28T08:00:00.0000000+00:00", "2026-09-28T09:00:00.0000000+00:00"), (version.GetProperty("validFrom").GetString(), version.GetProperty("validTo").GetString()));
    }

    [Fact]
    public async Task Import_LineWithoutValidTime_ValidFromCreation()
    {
        string line = """{"kind":"chunk","id":"x","scope":"general","projectId":null,"type":"fact","title":"Old export","content":"Before valid time.","importance":3,"pinned":false,"sourceRef":null,"createdBy":"user","createdAt":"2026-01-02T03:04:05.0000000+00:00","updatedAt":"2026-01-02T03:04:05.0000000+00:00","deletedAt":null}""";

        await Exporter(_database, _embeddings).ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(line + "\n")), Ct);

        ChunkId id = ChunkIds.Compute(TestExecutionContext.OwnerA, ChunkScope.General, null, ChunkType.Fact, "Old export");
        MemoryChunk chunk = (await _store.GetAsync(id, Ct))!;
        Assert.Equal((new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero), (DateTimeOffset?)null), (chunk.ValidFrom, chunk.ValidTo));
    }

    [Fact]
    public async Task Import_IntoEmptyDatabase_RoundTripsAndRecomputesEmbeddings()
    {
        await SeedAsync();
        string export = await ExportAsync(_database);
        await using TestDatabase target = await TestDatabase.CreateAsync();
        target.Accessor.Current = TestExecutionContext.Text();
        var embeddings = new FakeEmbeddingGenerator();

        Result<MemoryImportReport> report = await Exporter(target, embeddings).ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(export)), Ct);

        Assert.Equal(new MemoryImportReport(1, 3, 1, 2, 0), report.Value);
        Assert.Equal(export, await ExportAsync(target));
        ChunkId nozzle = ChunkIds.Compute(TestExecutionContext.OwnerA, ChunkScope.General, null, ChunkType.Device, "Voron nozzle size");
        Assert.Equal(await MemoryTestKit.EmbedAsync(embeddings, "Voron nozzle size\n0.6 mm"), await MemoryTestKit.StoredVectorAsync(target, nozzle));
        Assert.True((await MemoryTestKit.Store(target, embeddings, _time).GetAsync(nozzle, Ct))!.Pinned);
    }

    [Fact]
    public async Task Import_Twice_SkipsExistingRows()
    {
        await SeedAsync();
        string export = await ExportAsync(_database);

        Result<MemoryImportReport> report = await Exporter(_database, _embeddings).ImportAsync(new MemoryStream(Encoding.UTF8.GetBytes(export)), Ct);

        Assert.Equal(new MemoryImportReport(0, 0, 0, 0, 7), report.Value);
    }

    [Fact]
    public async Task Import_InvalidLine_ValidationErrorNamesLine()
    {
        Result<MemoryImportReport> report = await Exporter(_database, _embeddings).ImportAsync(new MemoryStream("{\"kind\":\"chunk\"}\n"u8.ToArray()), Ct);

        Assert.Equal("memory.invalid_import", report.Error.Code);
        Assert.Contains("line 1", report.Error.Message, StringComparison.Ordinal);
    }

    private async Task SeedAsync()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron", "3D printer build");
        ChunkId nozzle = (await _store.WriteAsync(new ChunkDraft(ChunkScope.General, null, ChunkType.Device, "Voron nozzle size", "0.4 mm", 4), ChunkAuthor.Agent, Ct)).Value.Id;
        _time.Advance(TimeSpan.FromHours(1));
        await _store.WriteAsync(new ChunkDraft(ChunkScope.General, null, ChunkType.Device, "Voron nozzle size", "0.6 mm", 4), ChunkAuthor.Agent, Ct);
        await _store.SetPinnedAsync(nozzle, pinned: true, Ct);
        ChunkId moved = (await _store.SupersedeAsync(nozzle, new ChunkDraft(ChunkScope.Project, voron, ChunkType.Device, "Nozzle size", "0.6 mm"), ChunkAuthor.User, Ct)).Value.Id;
        ChunkId oat = (await _store.WriteAsync(new ChunkDraft(ChunkScope.General, null, ChunkType.Preference, "Coffee", "Likes oat milk.", 2, "notes/coffee.md"), ChunkAuthor.User, Ct)).Value.Id;
        await _store.LinkAsync(oat, moved, ChunkEdgeKind.RelatesTo, Ct);
    }

    private NdjsonMemoryExporter Exporter(TestDatabase database, FakeEmbeddingGenerator embeddings) =>
        new(database.Factory, embeddings, database.Accessor);

    private async Task<string> ExportAsync(TestDatabase database)
    {
        using var output = new MemoryStream();
        await Exporter(database, _embeddings).ExportAsync(output, Ct);
        return Encoding.UTF8.GetString(output.ToArray());
    }
}
