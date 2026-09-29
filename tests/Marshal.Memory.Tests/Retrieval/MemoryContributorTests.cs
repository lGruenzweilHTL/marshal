using Marshal.Abstractions.Context;
using Marshal.Abstractions.Memory;
using Marshal.Abstractions.Satellites;
using Marshal.Memory.Projects;
using Marshal.Memory.Retrieval;
using Marshal.Memory.Tests.Projects;
using Marshal.Testing;
using Microsoft.Extensions.Time.Testing;
using static Marshal.Memory.Tests.Retrieval.HybridMemoryRetrieverTests;

namespace Marshal.Memory.Tests.Retrieval;

public sealed class MemoryContributorTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = new(new DateTimeOffset(2026, 9, 28, 8, 0, 0, TimeSpan.Zero));
    private readonly FakeEmbeddingGenerator _embeddings = new();
    private TestDatabase _database = null!;
    private SqliteMemoryStore _store = null!;
    private HybridMemoryRetriever _retriever = null!;
    private TurnMemory _turns = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text();
        _store = MemoryTestKit.Store(_database, _embeddings, _time);
        _retriever = MemoryTestKit.Retriever(_database, _embeddings, _time);
        _turns = new TurnMemory(_embeddings, new Areas(), ProjectTestKit.Activator(_database, _embeddings, new ProjectSessions(), new MemoryOptions(), _time));
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Retrieved_RelevantPreferenceInContextWithoutToolCall()
    {
        await WriteAsync("Coffee", "Likes oat milk.", Vec(0, 1), ChunkType.Preference);
        _embeddings.Pin("Make me a coffee", Vec(0, 1, 2, 0.3f));

        string? section = await Retrieved().ContributeAsync(new ContextRequest(TestExecutionContext.Text(), "Make me a coffee"), Ct);

        Assert.Contains("- [preference] Coffee: Likes oat milk.", section, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Pinned_UnrelatedTurn_StillPresent()
    {
        ChunkId allergy = await WriteAsync("Allergy", "Allergic to peanuts.", Vec(4, 1));
        await _store.SetPinnedAsync(allergy, pinned: true, Ct);
        _embeddings.Pin("What time is it?", Vec(11, 1));
        var request = new ContextRequest(TestExecutionContext.Text(), "What time is it?");

        string? pinned = await new PinnedMemoryContributor(_retriever).ContributeAsync(request, Ct);
        string? retrieved = await Retrieved().ContributeAsync(request, Ct);

        Assert.Contains("Allergic to peanuts.", pinned, StringComparison.Ordinal);
        Assert.Null(retrieved);
    }

    [Fact]
    public async Task TurnEmbedding_ComputedOncePerTurnWithAreaName()
    {
        var request = new ContextRequest(TestExecutionContext.Voice(areaId: "kitchen"), "Turn on the light");
        int before = _embeddings.CallCount;

        TurnMemoryContext first = await _turns.GetAsync(request, Ct);
        TurnMemoryContext second = await _turns.GetAsync(request, Ct);

        Assert.Same(first, second);
        Assert.Equal(before + 1, _embeddings.CallCount);
        Assert.Equal("Turn on the light (in Kitchen)", first.Text);
    }

    [Fact]
    public async Task TurnMemory_NamedProjectActiveWithSameEmbedding()
    {
        ProjectId voron = await MemoryTestKit.AddProjectAsync(_database, "Voron");
        int before = _embeddings.CallCount;

        TurnMemoryContext turn = await _turns.GetAsync(new ContextRequest(TestExecutionContext.Text(), "Is the Voron printing?"), Ct);

        Assert.Equal([voron], turn.ActiveProjects);
        Assert.Equal(ActivationReason.Name, Assert.Single(turn.Activations).Reason);
        Assert.Equal(before + 1, _embeddings.CallCount);
    }

    [Fact]
    public async Task Retrieved_NoUserText_NothingAndNoEmbedding()
    {
        int before = _embeddings.CallCount;

        Assert.Null(await Retrieved().ContributeAsync(new ContextRequest(TestExecutionContext.Text(), null), Ct));
        Assert.Equal(before, _embeddings.CallCount);
    }

    private RetrievedMemoryContributor Retrieved() =>
        new(_retriever, _turns, Microsoft.Extensions.Options.Options.Create(new MemoryOptions()));

    private async Task<ChunkId> WriteAsync(string title, string content, float[] vector, ChunkType type = ChunkType.Fact)
    {
        _embeddings.Pin($"{title}\n{content}", vector);
        return (await _store.WriteAsync(new ChunkDraft(ChunkScope.General, null, type, title, content), ChunkAuthor.User, Ct)).Value.Id;
    }

    private sealed class Areas : IAreaDirectory
    {
        public Task<(string? AreaName, string? FloorName)> DescribeAsync(string areaId, CancellationToken cancellationToken) =>
            Task.FromResult<(string?, string?)>((areaId == "kitchen" ? "Kitchen" : null, null));
    }
}
