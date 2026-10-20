using Marshal.Abstractions.Memory;
using Marshal.Data;
using Marshal.Data.Memory;
using Marshal.Memory.Consolidation;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Memory.Tests.Retrieval.HybridMemoryRetrieverTests;

namespace Marshal.Memory.Tests.Consolidation;

public sealed class MemoryAuditTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 1, 1, 8, 0, 0, TimeSpan.Zero));
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
    public async Task Stale_NotUpdatedOrRetrievedForThePeriodExceptPinned()
    {
        ChunkId old = await WriteAsync("Old router", "The old router is in the attic.");
        ChunkId pinned = await WriteAsync("Allergy", "Allergic to peanuts.");
        await _store.SetPinnedAsync(pinned, pinned: true, Ct);
        ChunkId retrieved = await WriteAsync("Coffee", "Likes oat milk.", Vec(0, 1));
        _time.Advance(TimeSpan.FromDays(150));
        _embeddings.Pin("coffee", Vec(0, 1));
        await MemoryTestKit.Retriever(_database, _embeddings, _time).RetrieveAsync(new MemoryQuery("coffee", null, new HashSet<ProjectId>(), 600), Ct);
        ChunkId updated = await WriteAsync("Printer", "Version one.");
        await WriteAsync("Printer", "Version two.");
        _time.Advance(TimeSpan.FromDays(40));

        MemoryAuditReport report = await Audit().RunAsync(Ct);

        Assert.Equal([old], report.Stale.Select(chunk => chunk.Id));
        Assert.DoesNotContain(retrieved, report.Stale.Select(chunk => chunk.Id));
        Assert.DoesNotContain(updated, report.Stale.Select(chunk => chunk.Id));
    }

    [Fact]
    public async Task Contradictions_ListedUntilSupersededOrDeleted()
    {
        ChunkId sixMm = await WriteAsync("Voron nozzle", "0.6 mm nozzle.");
        ChunkId fourMm = await WriteAsync("Voron nozzle (conflicting)", "0.4 mm nozzle.");
        await _store.LinkAsync(fourMm, sixMm, ChunkEdgeKind.Contradicts, Ct);
        ChunkId basement = await WriteAsync("Printer location", "Basement.");
        ChunkId garage = await WriteAsync("Printer room", "Garage.");
        await _store.LinkAsync(garage, basement, ChunkEdgeKind.Contradicts, Ct);
        ChunkId kitchen = await WriteAsync("Kettle", "In the kitchen.");
        ChunkId office = await WriteAsync("Kettle place", "In the office.");
        await _store.LinkAsync(office, kitchen, ChunkEdgeKind.Contradicts, Ct);

        MemoryAuditReport before = await Audit().RunAsync(Ct);
        await _store.LinkAsync(garage, basement, ChunkEdgeKind.Supersedes, Ct);
        await _store.DeleteAsync(office, Ct);
        MemoryAuditReport after = await Audit().RunAsync(Ct);

        Assert.Equal(3, before.Contradictions.Count);
        MemoryContradiction open = Assert.Single(after.Contradictions);
        Assert.Equal((fourMm, sixMm), (open.From.Id, open.To.Id));
    }

    [Fact]
    public async Task Orphans_ChunksOfDeletedProjectsAndDanglingEdges()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron");
        ChunkId calibration = await WriteAsync("Calibration", "Run the z offset macro.", project: voron);
        ChunkId live = await WriteAsync("Printer", "A Voron 2.4.");
        ChunkId gone = await WriteAsync("Old printer", "An Ender 3.");
        ChunkId replaced = await WriteAsync("Printer nozzle", "0.4 mm.");
        ChunkId successor = await WriteAsync("Voron nozzle size", "0.6 mm.");
        await _store.LinkAsync(live, gone, ChunkEdgeKind.RelatesTo, Ct);
        await _store.LinkAsync(successor, replaced, ChunkEdgeKind.Supersedes, Ct);
        await _store.DeleteAsync(gone, Ct);
        await _store.DeleteAsync(replaced, Ct);
        await using (MarshalDbContext db = _database.CreateContext())
        {
            Project project = await db.Projects.SingleAsync(Ct);
            project.DeletedAt = _time.GetUtcNow();
            await db.SaveChangesAsync(Ct);
        }

        MemoryAuditReport report = await Audit().RunAsync(Ct);

        Assert.Equal([calibration], report.Orphans.Select(chunk => chunk.Id));
        MemoryEdge dangling = Assert.Single(report.DanglingEdges);
        Assert.Equal((live, gone, ChunkEdgeKind.RelatesTo), (dangling.From, dangling.To, dangling.Kind));
    }

    private MemoryAudit Audit() =>
        new(_database.Factory, Microsoft.Extensions.Options.Options.Create(new MemoryOptions { Audit = { StaleAfterDays = 180 } }), _time, new Marshal.Memory.Idle.EfMemoryProposals(_database.Factory, MemoryTestKit.Store(_database, _embeddings, _time), _database.Accessor, _time));

    private async Task<ChunkId> WriteAsync(string title, string content, float[]? vector = null, ProjectId? project = null)
    {
        _embeddings.Pin($"{title}\n{content}", vector ?? Vec(9, 1));
        var draft = new ChunkDraft(project is null ? ChunkScope.General : ChunkScope.Project, project, ChunkType.Fact, title, content);
        return (await _store.WriteAsync(draft, ChunkAuthor.User, Ct)).Value.Id;
    }
}
