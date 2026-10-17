using Marshal.Abstractions.Context;
using Marshal.Abstractions.Execution;
using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Core.Tests.Tools;

public sealed class NamespaceToolSelectorTests
{
    private readonly FakeEmbeddingGenerator _embeddings = new(dimensions: 4);
    private readonly FixedTurnEmbedding _turn = new();
    private readonly List<IToolNamespaceHints> _hints = [];
    private readonly ToolsOptions _options = new() { Core = ["agent", "memory"], Retrieval = { MinTools = 3 } };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Prepare_CoreAlwaysLoaded()
    {
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "hello");

        Assert.Equal(["agent.find_tools", "agent.load_skill", "memory.recall"], Ids(offered));
    }

    [Fact]
    public async Task Prepare_RetrievalAboveThreshold_LoadsAtMostMaxPerTurnBestFirst()
    {
        _options.Retrieval.MaxPerTurn = 2;
        _turn.Vector = [1, 0, 0, 0];
        Describe("printer", 0.9f);
        Describe("music", 0.6f);
        Describe("calendar", 0.5f);
        Describe("mail", 0.1f);
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "how far is the print?");

        Assert.Equal(["agent", "memory", "printer", "music"], Namespaces(offered));
        LoadedToolNamespace printer = context.LoadedTools.All.Single(item => item.Name == "printer");
        Assert.Equal((LoadReasons.Retrieval, 0.9), (printer.Reason, Math.Round(printer.Score!.Value, 1)));
    }

    [Fact]
    public async Task Prepare_BelowThreshold_NotLoaded()
    {
        _turn.Vector = [1, 0, 0, 0];
        Describe("printer", 0.2f);
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "how far is the print?");

        Assert.DoesNotContain("printer", Namespaces(offered));
    }

    [Fact]
    public async Task Prepare_Hint_LoadsNamespace()
    {
        _hints.Add(new FixedHints("music"));
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "louder in the kitchen");

        Assert.Contains("music", Namespaces(offered));
        Assert.Equal(LoadReasons.Hint, context.LoadedTools.All.Single(item => item.Name == "music").Reason);
    }

    [Fact]
    public async Task Prepare_SessionNamespaces_StayLoaded()
    {
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "a bit louder", session: ["music"]);

        Assert.Contains("music", Namespaces(offered));
        Assert.Equal(LoadReasons.Session, context.LoadedTools.All.Single(item => item.Name == "music").Reason);
    }

    [Fact]
    public async Task Prepare_AtOrBelowMinTools_SkippedAndAllVisibleOffered()
    {
        _options.Retrieval.MinTools = 7;
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "hello");

        Assert.False(context.LoadedTools.RetrievalActive);
        Assert.Equal(6, offered.Count);
        Assert.Equal(7, visible.Count);
        Assert.DoesNotContain("agent.find_tools", Ids(offered));
    }

    [Fact]
    public async Task Prepare_Disabled_AllVisibleOffered()
    {
        _options.Retrieval.Enabled = false;
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "hello");

        Assert.Equal(6, offered.Count);
        Assert.Empty(context.LoadedTools.All);
    }

    [Fact]
    public async Task Prepare_NoTurnEmbedding_CoreAndHintsOnly()
    {
        _hints.Add(new FixedHints("music"));
        Describe("printer", 1f);
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();
        var selector = new NamespaceToolSelector(Microsoft.Extensions.Options.Options.Create(_options), _hints, new CachedNamespaceEmbeddings(_embeddings), _embeddings, NullLogger<NamespaceToolSelector>.Instance);

        await selector.PrepareAsync(visible, new ToolSelectionRequest(new ContextRequest(context, "print status in the kitchen"), []), Ct);

        Assert.Equal(["agent", "memory", "music"], Namespaces(selector.Select(visible, context)));
    }

    [Fact]
    public async Task Prepare_FilteredNamespace_NeverOffered()
    {
        _hints.Add(new FixedHints("mail"));
        _turn.Vector = [1, 0, 0, 0];
        Describe("mail", 1f);
        (IReadOnlyList<RegisteredTool> all, MarshalExecutionContext context) = await ToolsAsync();
        RegisteredTool[] visible = [.. all.Where(tool => tool.Id.Namespace != "mail")];

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "any new mail?");

        Assert.DoesNotContain("mail", Namespaces(offered));
        Assert.False(context.LoadedTools.Contains("mail"));
    }

    [Fact]
    public async Task Select_OrderIsCoreOrderThenLoadOrderThenToolName()
    {
        _hints.Add(new FixedHints("printer"));
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync("printer.status", "printer.cancel", "printer.snapshot");

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "print", session: ["music"]);

        Assert.Equal(["agent.find_tools", "agent.load_skill", "memory.recall", "music.play", "printer.cancel", "printer.snapshot", "printer.status"], Ids(offered));
    }

    [Fact]
    public async Task Find_LoadingAppendsOnly()
    {
        _options.Retrieval.MaxPerTurn = 1;
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();
        NamespaceToolSelector selector = Selector();
        await selector.PrepareAsync(visible, new ToolSelectionRequest(new ContextRequest(context, "hello"), ["music"]), Ct);
        IReadOnlyList<RegisteredTool> before = selector.Select(visible, context);

        IReadOnlyList<ToolNamespace> found = await selector.FindAsync("calendar", visible, context, Ct);
        IReadOnlyList<RegisteredTool> after = selector.Select(visible, context);

        Assert.Equal("calendar", found[0].Name);
        Assert.Equal(Ids(before), Ids(after).Take(before.Count));
        Assert.Contains("calendar.list_events", Ids(after).Skip(before.Count));
    }

    [Fact]
    public async Task Select_EveryVisibleNamespaceLoaded_FindToolsHidden()
    {
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();

        IReadOnlyList<RegisteredTool> offered = await SelectAsync(visible, context, "hello", session: ["music", "printer", "calendar", "mail"]);

        Assert.DoesNotContain("agent.find_tools", Ids(offered));
    }

    [Fact]
    public async Task Prepare_NotesListLoadedNamespacesWithReasons()
    {
        _hints.Add(new FixedHints("printer"));
        (IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context) = await ToolsAsync();
        var request = new ContextRequest(context, "print");

        await Selector().PrepareAsync(visible, new ToolSelectionRequest(request, []), Ct);

        Assert.Equal(
            [("agent", "core"), ("memory", "core"), ("printer", "hint")],
            request.Notes.All.Where(note => note.Kind == LoadReasons.NoteKind).Select(note => (note.Label, note.Detail!)));
    }

    private NamespaceToolSelector Selector() =>
        new(Microsoft.Extensions.Options.Options.Create(_options), _hints, new CachedNamespaceEmbeddings(_embeddings), _embeddings, NullLogger<NamespaceToolSelector>.Instance, _turn);

    private async Task<IReadOnlyList<RegisteredTool>> SelectAsync(IReadOnlyList<RegisteredTool> visible, MarshalExecutionContext context, string text, IReadOnlyList<string>? session = null)
    {
        NamespaceToolSelector selector = Selector();
        await selector.PrepareAsync(visible, new ToolSelectionRequest(new ContextRequest(context, text), session ?? []), Ct);
        return selector.Select(visible, context);
    }

    // Namespace descriptions are "<name> tools"; this pins one at the given cosine similarity to the turn vector [1,0,0,0].
    private void Describe(string name, float similarity) =>
        _embeddings.Pin($"{name} tools", similarity, MathF.Sqrt(1 - (similarity * similarity)), 0, 0);

    private static async Task<(IReadOnlyList<RegisteredTool> Visible, MarshalExecutionContext Context)> ToolsAsync(params string[] printerTools)
    {
        ITool[] tools =
        [
            new FakeTool("agent.load_skill"), new FakeTool("agent.find_tools"), new FakeTool("memory.recall"), new FakeTool("music.play"),
            .. (printerTools.Length == 0 ? ["printer.status"] : printerTools).Select(id => new FakeTool(id)),
            new FakeTool("calendar.list_events"), new FakeTool("mail.search"),
        ];
        var source = new FakeToolSource("test", true, tools);
        source.Descriptions.AddRange(tools.Select(tool => tool.Descriptor.Id.Namespace).Distinct().Select(name => new ToolNamespace(name, $"{name} tools")));
        ToolSnapshot snapshot = await new ToolRegistry([source]).RefreshAsync(Ct);
        return (snapshot.Tools, TestExecutionContext.Text() with { Tools = snapshot });
    }

    private static string[] Ids(IEnumerable<RegisteredTool> tools) => [.. tools.Select(tool => tool.Id.ToString())];

    private static string[] Namespaces(IEnumerable<RegisteredTool> tools) => [.. tools.Select(tool => tool.Id.Namespace).Distinct()];

    private sealed class FixedTurnEmbedding : ITurnEmbedding
    {
        public float[]? Vector { get; set; }

        public Task<ReadOnlyMemory<float>?> GetAsync(ContextRequest request, CancellationToken cancellationToken) =>
            Task.FromResult<ReadOnlyMemory<float>?>(Vector);
    }

    private sealed class FixedHints(params string[] names) : IToolNamespaceHints
    {
        public Task<IReadOnlyList<string>> GetHintsAsync(string turnText, MarshalExecutionContext context, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(names);
    }
}
