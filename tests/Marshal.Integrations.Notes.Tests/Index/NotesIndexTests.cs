using Marshal.Abstractions;
using Marshal.Abstractions.Notes;
using Marshal.Data;
using Marshal.Data.Notes;
using Marshal.Integrations.Notes.Index;
using Marshal.Testing;
using Microsoft.EntityFrameworkCore;

namespace Marshal.Integrations.Notes.Tests.Index;

public sealed class NotesIndexTests : IAsyncLifetime
{
    private readonly FakeNotes _notes = new();
    private TestDatabase _database = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync()
    {
        _database = await TestDatabase.CreateAsync();
        _database.Accessor.Current = TestExecutionContext.Text(TestExecutionContext.OwnerA);
        _notes.Files["projects/marshal.md"] = "# Marshal\nSelf-hosted agent.\n## Voice\nHA Assist is the main interface.\n## Web\nA Vue SPA.";
        _notes.Files["Reisen/Wien.md"] = "Ohne Überschrift.\n# Prater\nRiesenrad fahren.";
    }

    public async ValueTask DisposeAsync() => await _database.DisposeAsync();

    [Fact]
    public async Task Update_IndexesEverySectionOnce()
    {
        NotesIndexUpdate update = (await Index().UpdateAsync(_notes, Ct)).Value;

        Assert.Equal(["Reisen/Wien.md", "projects/marshal.md"], update.Reindexed.Order(StringComparer.Ordinal));
        Assert.Empty(update.Removed);
        List<NoteIndexEntry> entries = await EntriesAsync();
        Assert.Equal(
            [("Reisen/Wien.md", "Wien", "Ohne Überschrift."), ("Reisen/Wien.md", "Prater", "Riesenrad fahren."), ("projects/marshal.md", "Marshal", "Self-hosted agent."), ("projects/marshal.md", "Voice", "HA Assist is the main interface."), ("projects/marshal.md", "Web", "A Vue SPA.")],
            entries.OrderBy(entry => entry.Path, StringComparer.Ordinal).ThenBy(entry => entry.Position).Select(entry => (entry.Path, entry.Heading, entry.Text)));
    }

    [Fact]
    public async Task Update_RebuildsOnlyChangedFilesAndDropsRemovedOnes()
    {
        SqliteNotesIndex index = Index();
        await index.UpdateAsync(_notes, Ct);
        List<Guid> wien = [.. (await EntriesAsync()).Where(entry => entry.Path == "Reisen/Wien.md").Select(entry => entry.Id)];

        NotesIndexUpdate unchanged = (await index.UpdateAsync(_notes, Ct)).Value;
        _notes.Files["projects/marshal.md"] = "# Marshal\nNow with notes.";
        _notes.Files["old.md"] = "# Old";
        await index.UpdateAsync(_notes, Ct);
        _notes.Files.Remove("old.md");
        NotesIndexUpdate changed = (await index.UpdateAsync(_notes, Ct)).Value;

        Assert.Empty(unchanged.Reindexed);
        Assert.Empty(changed.Reindexed);
        Assert.Equal(["old.md"], changed.Removed);
        List<NoteIndexEntry> entries = await EntriesAsync();
        Assert.Equal(wien, entries.Where(entry => entry.Path == "Reisen/Wien.md").Select(entry => entry.Id));
        Assert.Equal("Now with notes.", Assert.Single(entries, entry => entry.Path == "projects/marshal.md").Text);
        Assert.DoesNotContain(await SearchAsync("Riesenrad Vue SPA old"), hit => hit.Path is "old.md" || hit.Text == "A Vue SPA.");
    }

    [Fact]
    public async Task Search_KeywordHit()
    {
        await Index().UpdateAsync(_notes, Ct);

        NoteHit hit = (await SearchAsync("Assist"))[0];

        Assert.Equal(("projects/marshal.md", "Voice", "HA Assist is the main interface."), (hit.Path, hit.Heading, hit.Text));
    }

    [Fact]
    public async Task Search_VectorHitWithoutSharedWords()
    {
        FakeEmbeddingGenerator embeddings = new FakeEmbeddingGenerator(4, "fake-model")
            .Pin("Reisen/Wien.md\nPrater\nRiesenrad fahren.", 1, 0, 0, 0)
            .Pin("ferris wheel", 1, 0.05f, 0, 0);
        await Index(embeddings).UpdateAsync(_notes, Ct);

        NoteHit hit = (await Index(embeddings).SearchAsync("ferris wheel", 3, Ct))[0];

        Assert.Equal(("Reisen/Wien.md", "Prater"), (hit.Path, hit.Heading));
        Assert.True(hit.Score > 0.9);
    }

    [Fact]
    public async Task Search_OnlyTheOwnersNotes()
    {
        await Index().UpdateAsync(_notes, Ct);

        _database.Accessor.Current = TestExecutionContext.Text(TestExecutionContext.OwnerB);

        Assert.Empty(await SearchAsync("Assist Riesenrad"));
    }

    [Fact]
    public async Task EmbeddingModelChange_RebuildsTheWholeIndex()
    {
        await Index(new FakeEmbeddingGenerator(16, "model-a")).UpdateAsync(_notes, Ct);

        var other = new FakeEmbeddingGenerator(8, "model-b");
        NotesIndexUpdate update = (await Index(other).UpdateAsync(_notes, Ct)).Value;

        Assert.Equal(2, update.Reindexed.Count);
        Assert.NotEmpty(await Index(other).SearchAsync("Riesenrad", 3, Ct));
    }

    [Fact]
    public async Task UnreadableFile_SkippedOthersIndexed()
    {
        _notes.Broken.Add("Reisen/Wien.md");

        NotesIndexUpdate update = (await Index().UpdateAsync(_notes, Ct)).Value;

        Assert.Equal(["projects/marshal.md"], update.Reindexed);
    }

    private SqliteNotesIndex Index(FakeEmbeddingGenerator? embeddings = null) =>
        new(_database.Factory, embeddings ?? new FakeEmbeddingGenerator(16, "fake-model"), _database.Accessor, TimeProvider.System);

    private Task<IReadOnlyList<NoteHit>> SearchAsync(string query) => Index().SearchAsync(query, 5, Ct);

    private async Task<List<NoteIndexEntry>> EntriesAsync()
    {
        await using MarshalDbContext db = _database.CreateContext();
        return await db.NoteIndexEntries.AsNoTracking().ToListAsync(Ct);
    }

    private sealed class FakeNotes : INotesSource
    {
        public Dictionary<string, string> Files { get; } = new(StringComparer.Ordinal);

        public HashSet<string> Broken { get; } = [];

        public bool IsConfigured => true;

        public Task<Result<IReadOnlyList<NoteFile>>> ListAsync(string? folder, CancellationToken cancellationToken) =>
            Task.FromResult<Result<IReadOnlyList<NoteFile>>>(Files.Keys.Order(StringComparer.Ordinal).Select(path => new NoteFile(path, Files[path].Length, DateTimeOffset.UnixEpoch)).ToList());

        public Task<Result<string>> ReadAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<Result<string>>(Broken.Contains(path) ? NotesErrors.NotFound(path) : Files[path]);

        public Task<Result> SyncAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }
}
