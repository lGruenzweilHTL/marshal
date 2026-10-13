using Marshal.Abstractions;
using Marshal.Abstractions.Notes;
using Marshal.Abstractions.Rss;
using Marshal.Abstractions.Tools;
using Marshal.Integrations.Notes.Index;
using Marshal.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Marshal.Demo.Tests;

public sealed class RssNotesDemoTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DemoOff_RssAndNotesStayUnconfigured()
    {
        await using var demo = DemoServices.Create(demo: false);

        Assert.False(demo.Get<IRssClient>().IsConfigured);
        Assert.False(demo.Get<INotesSource>().IsConfigured);
    }

    [Fact]
    public async Task NothingConfigured_RssAndNotesSimulated()
    {
        await using var demo = DemoServices.Create();

        Assert.True(demo.Get<IRssClient>().IsConfigured);
        Assert.True(demo.Get<INotesSource>().IsConfigured);
        Assert.Contains(DemoIntegration.Rss, demo.Get<DemoIntegrations>().SimulatedAtStartup);
        Assert.Contains(DemoIntegration.Notes, demo.Get<DemoIntegrations>().SimulatedAtStartup);
    }

    [Fact]
    public async Task RssTools_ReturnDemoArticlesAndFullText()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        string unread = await DemoServices.InvokeAsync(tools["rss.get_unread"], """{"category":"3D printing"}""");
        string found = await DemoServices.InvokeAsync(tools["rss.search"], """{"query":"voice satellite"}""");
        string full = await DemoServices.InvokeAsync(tools["rss.fetch_full_text"], """{"articleId":"1760000000000002"}""");
        await DemoServices.InvokeAsync(tools["rss.mark_read"], """{"ids":["1760000000000001"]}""");
        string after = await DemoServices.InvokeAsync(tools["rss.get_unread"], "{}");

        Assert.Contains("Klipper 0.13 released", unread);
        Assert.DoesNotContain("energy dashboard", unread);
        Assert.Contains("Building a voice satellite", found);
        Assert.Contains("Drying PETG", full);
        Assert.DoesNotContain("Klipper 0.13 released", after);
    }

    [Fact]
    public async Task FullText_LinkOutsideTheDemoFeeds_Fails()
    {
        await using var demo = DemoServices.Create();

        Result<FullText> text = await demo.Get<IFullTextExtractor>().ExtractAsync(new Uri("https://example.org/other"), Ct);

        Assert.Equal("rss.full_text_failed", text.Error.Code);
    }

    [Fact]
    public async Task RssConfigured_RealClientUsed()
    {
        var real = new ConfiguredRss();
        await using var demo = DemoServices.Create(configure: services => services.Replace(ServiceDescriptor.Singleton<IRssClient>(real)));
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        string feeds = await DemoServices.InvokeAsync(tools["rss.list_feeds"], "{}");

        Assert.Contains("Real feed", feeds);
        Assert.DoesNotContain(DemoIntegration.Rss, demo.Get<DemoIntegrations>().SimulatedAtStartup);
    }

    [Fact]
    public async Task NotesTools_ListAndReadTheSampleNotes()
    {
        await using var demo = DemoServices.Create();
        Dictionary<string, ITool> tools = await demo.ToolsAsync();

        string list = await DemoServices.InvokeAsync(tools["notes.list"], """{"folder":"recipes"}""");
        string note = await DemoServices.InvokeAsync(tools["notes.read"], """{"path":"recipes/pizza-dough.md"}""");

        Assert.Contains("recipes/pizza-dough.md", list);
        Assert.DoesNotContain("printing/", list);
        Assert.Contains("325 g water", note);
    }

    [Fact]
    public async Task Notes_PathOutsideTheRepository_Invalid()
    {
        await using var demo = DemoServices.Create();

        Result<string> read = await demo.Get<INotesSource>().ReadAsync("../secrets.md", Ct);

        Assert.Equal("notes.path_invalid", read.Error.Code);
    }

    [Fact]
    public async Task NotesIndex_IndexesTheSampleNotes()
    {
        await using var demo = DemoServices.Create();
        await using TestDatabase database = await TestDatabase.CreateAsync();
        database.Accessor.Current = TestExecutionContext.Text();
        var index = new SqliteNotesIndex(database.Factory, new FakeEmbeddingGenerator(), database.Accessor, demo.Time);

        NotesIndexUpdate update = (await index.UpdateAsync(demo.Get<INotesSource>(), Ct)).Value;
        IReadOnlyList<NoteHit> hits = await index.SearchAsync("pizza dough flour", 5, Ct);

        Assert.Equal(demo.World.Notes.Files.Count, update.Reindexed.Count);
        Assert.Contains(hits, hit => hit.Path == "recipes/pizza-dough.md");
    }

    [Fact]
    public async Task NotesConfigured_RealSourceUsed()
    {
        await using var demo = DemoServices.Create(configure: services => services.Replace(ServiceDescriptor.Singleton<INotesSource>(new ConfiguredNotes())));

        Result<IReadOnlyList<NoteFile>> files = await demo.Get<INotesSource>().ListAsync(null, Ct);

        Assert.Equal(["real.md"], files.Value.Select(file => file.Path));
        Assert.DoesNotContain(DemoIntegration.Notes, demo.Get<DemoIntegrations>().SimulatedAtStartup);
    }

    private sealed class ConfiguredRss : IRssClient
    {
        public bool IsConfigured => true;

        public Task<Result<IReadOnlyList<RssFeed>>> ListFeedsAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<RssFeed>>([new RssFeed("feed/1", "Real feed", null, null, [])]));

        public Task<Result<IReadOnlyList<RssCategory>>> ListCategoriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<RssCategory>>([]));

        public Task<Result<IReadOnlyList<RssArticle>>> GetUnreadAsync(string? category, DateTimeOffset? since, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<RssArticle>>([]));

        public Task<Result<RssArticle>> GetArticleAsync(string id, CancellationToken cancellationToken) =>
            Task.FromResult<Result<RssArticle>>(RssErrors.ArticleNotFound(id));

        public Task<Result<IReadOnlyList<RssArticle>>> SearchAsync(string query, int limit, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<RssArticle>>([]));

        public Task<Result> MarkReadAsync(IReadOnlyList<string> ids, CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }

    private sealed class ConfiguredNotes : INotesSource
    {
        public bool IsConfigured => true;

        public Task<Result<IReadOnlyList<NoteFile>>> ListAsync(string? folder, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<IReadOnlyList<NoteFile>>([new NoteFile("real.md", 1, DateTimeOffset.UnixEpoch)]));

        public Task<Result<string>> ReadAsync(string path, CancellationToken cancellationToken) => Task.FromResult<Result<string>>("real");

        public Task<Result> SyncAsync(CancellationToken cancellationToken) => Task.FromResult(Result.Success());
    }
}
