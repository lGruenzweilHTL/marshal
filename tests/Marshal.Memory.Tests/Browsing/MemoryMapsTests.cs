using Marshal.Abstractions.Memory;
using Marshal.Memory.Browsing;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Memory.Tests.Retrieval.HybridMemoryRetrieverTests;

namespace Marshal.Memory.Tests.Browsing;

public sealed class MemoryMapsTests : IAsyncLifetime
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
    public async Task Graph_LiveChunksAndEdgesBetweenThem()
    {
        ChunkId printer = await WriteAsync("Printer", Vec(0, 1));
        ChunkId nozzle = await WriteAsync("Nozzle", Vec(1, 1));
        ChunkId gone = await WriteAsync("Old printer", Vec(2, 1));
        await _store.LinkAsync(nozzle, printer, ChunkEdgeKind.DependsOn, Ct);
        await _store.LinkAsync(printer, gone, ChunkEdgeKind.RelatesTo, Ct);
        await _store.DeleteAsync(gone, Ct);
        await _store.SetPinnedAsync(printer, pinned: true, Ct);

        MemoryGraph graph = await Maps().GetGraphAsync(Ct);

        Assert.Equal(new HashSet<ChunkId> { nozzle, printer }, graph.Nodes.Select(node => node.Id).ToHashSet());
        Assert.True(graph.Nodes.Single(node => node.Id == printer).Pinned);
        MemoryEdge edge = Assert.Single(graph.Edges);
        Assert.Equal((nozzle, printer, ChunkEdgeKind.DependsOn), (edge.From, edge.To, edge.Kind));
    }

    [Fact]
    public async Task Neighbourhood_NeighboursInBothDirections()
    {
        ChunkId a = await WriteAsync("A", Vec(0, 1));
        ChunkId b = await WriteAsync("B", Vec(1, 1));
        ChunkId c = await WriteAsync("C", Vec(2, 1));
        ChunkId unrelated = await WriteAsync("Unrelated", Vec(3, 1));
        await _store.LinkAsync(a, b, ChunkEdgeKind.DependsOn, Ct);
        await _store.LinkAsync(c, a, ChunkEdgeKind.Supersedes, Ct);

        MemoryGraph? graph = await Maps().GetNeighbourhoodAsync(a, Ct);

        Assert.NotNull(graph);
        Assert.Equal(new HashSet<ChunkId> { a, b, c }, graph.Nodes.Select(node => node.Id).ToHashSet());
        Assert.DoesNotContain(graph.Nodes, node => node.Id == unrelated);
        Assert.Equal(
            new HashSet<(ChunkId, ChunkId, ChunkEdgeKind)> { (a, b, ChunkEdgeKind.DependsOn), (c, a, ChunkEdgeKind.Supersedes) },
            graph.Edges.Select(edge => (edge.From, edge.To, edge.Kind)).ToHashSet());
    }

    [Fact]
    public async Task Neighbourhood_IncludesEdgesBetweenNeighbours()
    {
        ChunkId a = await WriteAsync("A", Vec(0, 1));
        ChunkId b = await WriteAsync("B", Vec(1, 1));
        ChunkId c = await WriteAsync("C", Vec(2, 1));
        ChunkId outside = await WriteAsync("Outside", Vec(3, 1));
        await _store.LinkAsync(a, b, ChunkEdgeKind.RelatesTo, Ct);
        await _store.LinkAsync(a, c, ChunkEdgeKind.RelatesTo, Ct);
        await _store.LinkAsync(b, c, ChunkEdgeKind.RelatesTo, Ct);
        await _store.LinkAsync(b, outside, ChunkEdgeKind.RelatesTo, Ct);

        MemoryGraph? graph = await Maps().GetNeighbourhoodAsync(a, Ct);

        Assert.NotNull(graph);
        Assert.Equal(new HashSet<ChunkId> { a, b, c }, graph.Nodes.Select(node => node.Id).ToHashSet());
        Assert.Contains(graph.Edges, edge => edge.From == b && edge.To == c);
        Assert.DoesNotContain(graph.Edges, edge => edge.To == outside);
        Assert.Equal(3, graph.Edges.Count);
    }

    [Fact]
    public async Task Neighbourhood_LeavesOutDeletedNeighbour()
    {
        ChunkId a = await WriteAsync("A", Vec(0, 1));
        ChunkId b = await WriteAsync("B", Vec(1, 1));
        ChunkId gone = await WriteAsync("Gone", Vec(2, 1));
        await _store.LinkAsync(a, b, ChunkEdgeKind.RelatesTo, Ct);
        await _store.LinkAsync(a, gone, ChunkEdgeKind.RelatesTo, Ct);
        await _store.LinkAsync(gone, b, ChunkEdgeKind.RelatesTo, Ct);
        await _store.DeleteAsync(gone, Ct);

        MemoryGraph? graph = await Maps().GetNeighbourhoodAsync(a, Ct);

        Assert.NotNull(graph);
        Assert.Equal(new HashSet<ChunkId> { a, b }, graph.Nodes.Select(node => node.Id).ToHashSet());
        MemoryEdge edge = Assert.Single(graph.Edges);
        Assert.Equal((a, b), (edge.From, edge.To));
    }

    [Fact]
    public async Task Neighbourhood_OfChunkWithoutLinks_IsTheChunkAlone()
    {
        ChunkId a = await WriteAsync("A", Vec(0, 1));

        MemoryGraph? graph = await Maps().GetNeighbourhoodAsync(a, Ct);

        Assert.NotNull(graph);
        Assert.Equal(a, Assert.Single(graph.Nodes).Id);
        Assert.Empty(graph.Edges);
    }

    [Fact]
    public async Task Neighbourhood_MissingDeletedOrOtherOwnersChunk_IsNull()
    {
        ChunkId deleted = await WriteAsync("Deleted", Vec(0, 1));
        await _store.DeleteAsync(deleted, Ct);
        ChunkId foreign;
        using (_database.Accessor.Use(TestExecutionContext.Text(TestExecutionContext.OwnerB)))
            foreign = await WriteAsync("Someone else", Vec(1, 1));
        var missing = ChunkId.From(new string('a', ChunkId.Length));

        Assert.Null(await Maps().GetNeighbourhoodAsync(missing, Ct));
        Assert.Null(await Maps().GetNeighbourhoodAsync(deleted, Ct));
        Assert.Null(await Maps().GetNeighbourhoodAsync(foreign, Ct));
    }

    [Fact]
    public async Task Scatter_OwnLiveChunksProjectedApartByTopic()
    {
        ChunkId coffee = await WriteAsync("Coffee", Vec(0, 1));
        ChunkId tea = await WriteAsync("Tea", Vec(0, 0.9f, 1, 0.1f));
        ChunkId printer = await WriteAsync("Printer", Vec(5, 1));
        ChunkId nozzle = await WriteAsync("Nozzle", Vec(5, 0.9f, 6, 0.1f));
        ChunkId deleted = await WriteAsync("Old printer", Vec(5, 1, 7, 0.2f));
        await _store.DeleteAsync(deleted, Ct);
        using (_database.Accessor.Use(TestExecutionContext.Text(TestExecutionContext.OwnerB)))
            await WriteAsync("Someone else", Vec(9, 1));

        IReadOnlyList<MemoryScatterPoint> points = await Maps().GetScatterAsync(Ct);

        Assert.Equal(new HashSet<ChunkId> { coffee, nozzle, printer, tea }, points.Select(point => point.Id).ToHashSet());
        double Distance(ChunkId a, ChunkId b)
        {
            MemoryScatterPoint first = points.Single(point => point.Id == a);
            MemoryScatterPoint second = points.Single(point => point.Id == b);
            return Math.Sqrt(Math.Pow(first.X - second.X, 2) + Math.Pow(first.Y - second.Y, 2));
        }

        Assert.True(Distance(coffee, tea) < Distance(coffee, printer));
        Assert.True(Distance(printer, nozzle) < Distance(tea, nozzle));
    }

    private MemoryMaps Maps() => new(_database.Factory, _database.Accessor);

    private async Task<ChunkId> WriteAsync(string title, float[] vector)
    {
        _embeddings.Pin($"{title}\n{title} content", vector);
        return (await _store.WriteAsync(new ChunkDraft(ChunkScope.General, null, ChunkType.Fact, title, $"{title} content"), ChunkAuthor.User, Ct)).Value.Id;
    }
}
