using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Models;
using Marshal.Abstractions.Templates;
using Marshal.Abstractions.Tools;
using Marshal.Core.Configuration;
using Marshal.Core.Templates;
using Marshal.Testing;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Core.Tests.Templates;

public sealed class FileTemplateCatalogTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-templates-").FullName;
    private ConfigDirectoryWatcher? _watcher;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Load_FrontmatterAndPrompt()
    {
        Write("rss-scorer.md", """
            ---
            name: rss-scorer
            description: Scores RSS items for relevance.
            modelTier: fast
            tools: [rss.*, agent.load_skill]
            skills: [rss-digest]
            maxSteps: 4
            maxAdvisorCalls: 0
            ---
            Score each item from 0 to 3.
            """);

        using FileTemplateCatalog catalog = Load();

        AgentTemplate template = catalog.Find("rss-scorer")!;
        Assert.Equal("Scores RSS items for relevance.", template.Description);
        Assert.Equal(ModelTier.Fast, template.ModelTier);
        Assert.Equal(["rss.*", "agent.load_skill"], template.Tools);
        Assert.Equal(["rss-digest"], template.Skills);
        Assert.Equal((4, 0), (template.MaxSteps, template.MaxAdvisorCalls));
        Assert.Equal("Score each item from 0 to 3.", template.Prompt.Trim());
        Assert.Empty(catalog.Errors);
    }

    [Fact]
    public void Allowlist_GlobsOnToolIds()
    {
        Write("printer.md", "---\nname: printer\ndescription: Printer jobs.\ntools: [printer.*, agent.load_skill]\n---\n");

        using FileTemplateCatalog catalog = Load();
        AgentTemplate template = catalog.Find("printer")!;

        Assert.True(template.Allows(new ToolId("printer", "status")));
        Assert.True(template.Allows(new ToolId("agent", "load_skill")));
        Assert.False(template.Allows(new ToolId("agent", "delegate")));
        Assert.False(template.Allows(new ToolId("mail", "search")));
    }

    [Fact]
    public void Default_BuiltInWhenNoFile()
    {
        using FileTemplateCatalog catalog = Load();

        AgentTemplate template = ((ITemplateCatalog)catalog).For(TestExecutionContext.Text());

        Assert.Equal(AgentTemplate.BuiltInDefault, template);
        Assert.True(template.Allows(new ToolId("mail", "search")));
        Assert.Equal(["default"], catalog.Templates.Select(candidate => candidate.Name));
    }

    [Fact]
    public void Default_FileOverridesBuiltIn()
    {
        Write("default.md", "---\nname: default\ndescription: Home assistant.\ntools: [\"ha.*\", \"timers.*\"]\n---\nPrefer short answers.");

        using FileTemplateCatalog catalog = Load();

        AgentTemplate template = ((ITemplateCatalog)catalog).For(TestExecutionContext.Text());
        Assert.Equal(["ha.*", "timers.*"], template.Tools);
        Assert.Equal("Prefer short answers.", template.Prompt);
    }

    [Theory]
    [InlineData("other.md", "---\nname: mismatch\ndescription: d\ntools: []\n---\n", "file")]
    [InlineData("nodesc.md", "---\nname: nodesc\ntools: []\n---\n", "description")]
    [InlineData("tier.md", "---\nname: tier\ndescription: d\nmodelTier: huge\ntools: []\n---\n", "modelTier")]
    [InlineData("advisor.md", "---\nname: advisor\ndescription: d\nmodelTier: advisor\ntools: []\n---\n", "modelTier")]
    [InlineData("steps.md", "---\nname: steps\ndescription: d\nmaxSteps: 0\ntools: []\n---\n", "maxSteps")]
    [InlineData("unknown.md", "---\nname: unknown\ndescription: d\ncolor: red\ntools: []\n---\n", "color")]
    [InlineData("plain.md", "no frontmatter", "frontmatter")]
    [InlineData("Upper.md", "---\nname: Upper\ndescription: d\ntools: []\n---\n", "name")]
    public void Load_InvalidTemplate_RejectedOthersLoad(string file, string content, string expected)
    {
        Write("good.md", "---\nname: good\ndescription: Good.\ntools: []\n---\n");
        Write(file, content);

        using FileTemplateCatalog catalog = Load();

        Assert.NotNull(catalog.Find("good"));
        ConfigFileError error = Assert.Single(catalog.Errors);
        Assert.Equal(file, error.File);
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_LiteralSecret_Rejected()
    {
        Write("leak.md", "---\nname: leak\ndescription: d\ntools: []\n---\nUse ghp_abcdefghijklmnopqrstuvwxyz0123456789 for GitHub.");

        using FileTemplateCatalog catalog = Load();

        Assert.Null(catalog.Find("leak"));
        Assert.True(Assert.Single(catalog.Errors).LiteralSecret);
    }

    [Fact]
    public async Task TemplateAddedOnDisk_Reloads()
    {
        using FileTemplateCatalog catalog = Load(watch: true);

        Write("late.md", "---\nname: late\ndescription: Later.\ntools: []\n---\n");

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (catalog.Find("late") is null && DateTime.UtcNow < deadline)
            await Task.Delay(50, Ct);

        Assert.NotNull(catalog.Find("late"));
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        Directory.Delete(_directory, recursive: true);
    }

    private FileTemplateCatalog Load(bool watch = false)
    {
        _watcher = watch ? new ConfigDirectoryWatcher(_directory) : null;
        return new FileTemplateCatalog(_directory, _watcher, "*.md", new ConfigSecretScanner(), NullLogger<FileTemplateCatalog>.Instance);
    }

    private void Write(string file, string content) => File.WriteAllText(Path.Combine(_directory, file), content);
}
