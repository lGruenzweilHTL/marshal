using Marshal.Abstractions.Tools;
using Marshal.Core.Tools;
using Marshal.Testing;

namespace Marshal.Core.Tests.Tools;

public sealed class ToolRegistryTests
{
    [Fact]
    public async Task Refresh_MapsIdAndModelNameBothWays()
    {
        ToolRegistry registry = await RegistryAsync(new FakeToolSource("built-in", true, FakeTool.Safe("agent.ask_advisor")));

        RegisteredTool byName = Assert.IsType<RegisteredTool>(registry.FindByModelName("agent_ask_advisor"));
        RegisteredTool byId = Assert.IsType<RegisteredTool>(registry.FindById(new ToolId("agent", "ask_advisor")));

        Assert.Equal("agent.ask_advisor", byName.Id.ToString());
        Assert.Equal("agent_ask_advisor", byId.ModelName);
    }

    [Fact]
    public async Task FindByModelName_Unknown_ReturnsNull()
    {
        ToolRegistry registry = await RegistryAsync(new FakeToolSource("built-in", true, FakeTool.Safe("agent.ask_advisor")));

        Assert.Null(registry.FindByModelName("agent_unknown"));
    }

    [Theory]
    [InlineData("Agent")]
    [InlineData("my_ns")]
    [InlineData("my-ns")]
    [InlineData("1ns")]
    public void ToolId_InvalidNamespace_Rejected(string ns)
    {
        Assert.Throws<ArgumentException>(() => new ToolId(ns, "tool"));
        Assert.True(ToolId.Parse($"{ns}.tool").IsFailure);
    }

    [Theory]
    [InlineData("agent")]
    [InlineData("memory")]
    [InlineData("tasks")]
    [InlineData("notifications")]
    [InlineData("timers")]
    [InlineData("mail")]
    [InlineData("rss")]
    [InlineData("printer")]
    [InlineData("calendar")]
    [InlineData("notes")]
    [InlineData("places")]
    [InlineData("energy")]
    [InlineData("artifacts")]
    [InlineData("infra")]
    [InlineData("repos")]
    [InlineData("media")]
    [InlineData("music")]
    public async Task Refresh_ExternalSourceInReservedNamespace_RejectedOthersKeepWorking(string ns)
    {
        ToolRegistry registry = await RegistryAsync(
            new FakeToolSource("built-in", true, FakeTool.Safe("agent.ask_advisor")),
            new FakeToolSource("evil", false, FakeTool.Safe($"{ns}.send"), FakeTool.Safe("evil.other")));

        Assert.Null(registry.FindById(new ToolId(ns, "send")));
        Assert.Null(registry.FindById(new ToolId("evil", "other")));
        Assert.NotNull(registry.FindById(new ToolId("agent", "ask_advisor")));
        ToolSourceError error = Assert.Single(registry.Errors);
        Assert.Equal("evil", error.Source);
        Assert.Contains(ns, error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_BuiltInSourcesMayShareReservedNamespace()
    {
        ToolRegistry registry = await RegistryAsync(
            new FakeToolSource("core", true, FakeTool.Safe("agent.ask_advisor")),
            new FakeToolSource("tasks", true, FakeTool.Safe("agent.move_to_background")));

        Assert.Equal(2, registry.Tools.Count);
        Assert.Empty(registry.Errors);
    }

    [Fact]
    public async Task Refresh_ExternalName_SanitizesCharacters()
    {
        ToolRegistry registry = await RegistryAsync(new FakeToolSource("ha", false, FakeTool.Safe("ha.Get Live/Context.v2")));

        RegisteredTool tool = Assert.Single(registry.Tools);
        Assert.Equal("ha_Get_Live_Context_v2", tool.ModelName);
        Assert.Same(tool, registry.FindByModelName("ha_Get_Live_Context_v2"));
    }

    [Fact]
    public async Task Refresh_LongName_TruncatedWithHashAndMapsBack()
    {
        string name = new('x', 77);
        ToolRegistry registry = await RegistryAsync(new FakeToolSource("srv", false, FakeTool.Safe($"srv.{name}")));

        RegisteredTool tool = Assert.Single(registry.Tools);
        Assert.True(tool.ModelName.Length <= 64);
        Assert.Matches("^srv_x+_[0-9a-f]{8}$", tool.ModelName);
        Assert.Equal($"srv.{name}", registry.FindByModelName(tool.ModelName)?.Id.ToString());
    }

    [Fact]
    public async Task Refresh_LongNamesWithSamePrefix_GetDifferentHashes()
    {
        ToolRegistry registry = await RegistryAsync(new FakeToolSource("srv", false,
            FakeTool.Safe($"srv.{new string('x', 70)}a"),
            FakeTool.Safe($"srv.{new string('x', 70)}b")));

        Assert.Equal(2, registry.Tools.Select(tool => tool.ModelName).Distinct().Count());
    }

    [Fact]
    public async Task Refresh_ModelNameCollision_RejectsLaterSourceNamingBoth()
    {
        ToolRegistry registry = await RegistryAsync(
            new FakeToolSource("first", false, FakeTool.Safe("srv.a_b")),
            new FakeToolSource("second", false, FakeTool.Safe("other.x"), FakeTool.Safe("srv.a/b")));

        Assert.Equal("srv.a_b", Assert.Single(registry.Tools).Id.ToString());
        ToolSourceError error = Assert.Single(registry.Errors);
        Assert.Equal("second", error.Source);
        Assert.Contains("first", error.Message, StringComparison.Ordinal);
        Assert.Contains("second", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Refresh_ExternalNamespaceUsedByOtherSource_RejectsLaterSource()
    {
        ToolRegistry registry = await RegistryAsync(
            new FakeToolSource("srv", false, FakeTool.Safe("srv.a")),
            new FakeToolSource("copy", false, FakeTool.Safe("srv.b")));

        Assert.Equal("srv.a", Assert.Single(registry.Tools).Id.ToString());
        Assert.Equal("copy", Assert.Single(registry.Errors).Source);
    }

    [Fact]
    public async Task Refresh_DuplicateIdWithinSource_RejectsSource()
    {
        ToolRegistry registry = await RegistryAsync(new FakeToolSource("srv", false, FakeTool.Safe("srv.a"), FakeTool.Safe("srv.a")));

        Assert.Empty(registry.Tools);
        Assert.Single(registry.Errors);
    }

    [Fact]
    public async Task Refresh_SourceChanged_ReplacesTools()
    {
        var source = new FakeToolSource("srv", false, FakeTool.Safe("srv.a"));
        ToolRegistry registry = await RegistryAsync(source);
        source.Tools.Clear();
        source.Tools.Add(FakeTool.Safe("srv.b"));

        await registry.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("srv.b", Assert.Single(registry.Tools).Id.ToString());
    }

    [Fact]
    public async Task Refresh_ProviderSource_RegistersAndUnregistersAsUnit()
    {
        var provider = new FakeToolSourceProvider();
        var server = new FakeToolSource("srv", false, FakeTool.Safe("srv.a"), FakeTool.Safe("srv.b"));
        provider.Sources.Add(server);
        var registry = new ToolRegistry([new FakeToolSource("built-in", true, FakeTool.Safe("agent.ask_advisor"))], [provider]);

        await registry.RefreshAsync(TestContext.Current.CancellationToken);
        Assert.Equal(["agent.ask_advisor", "srv.a", "srv.b"], registry.Tools.Select(tool => tool.Id.ToString()));

        provider.Sources.Remove(server);
        await registry.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Equal("agent.ask_advisor", Assert.Single(registry.Tools).Id.ToString());
        Assert.Null(registry.FindByModelName("srv_a"));
    }

    [Fact]
    public async Task Refresh_ReturnsSnapshot_LaterRefreshLeavesItUnchanged()
    {
        var source = new FakeToolSource("srv", false, FakeTool.Safe("srv.a"));
        var registry = new ToolRegistry([source]);

        ToolSnapshot first = await registry.RefreshAsync(TestContext.Current.CancellationToken);
        source.Tools.Add(FakeTool.Safe("srv.b"));
        ToolSnapshot second = await registry.RefreshAsync(TestContext.Current.CancellationToken);

        Assert.Single(first.Tools);
        Assert.Null(first.FindByModelName("srv_b"));
        Assert.Equal(2, second.Tools.Count);
        Assert.Same(second, registry.Current);
    }

    [Fact]
    public async Task Refresh_SourceThrows_RejectsOnlyThatSource()
    {
        ToolRegistry registry = await RegistryAsync(
            new ThrowingSource(),
            new FakeToolSource("built-in", true, FakeTool.Safe("agent.ask_advisor")));

        Assert.Single(registry.Tools);
        Assert.Equal("broken", Assert.Single(registry.Errors).Source);
    }

    private static async Task<ToolRegistry> RegistryAsync(params IToolSource[] sources)
    {
        var registry = new ToolRegistry(sources);
        await registry.RefreshAsync(TestContext.Current.CancellationToken);
        return registry;
    }

    private sealed class ThrowingSource : IToolSource
    {
        public string Name => "broken";

        public bool IsBuiltIn => false;

        public Task<IReadOnlyList<ITool>> GetToolsAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("server down");
    }
}
