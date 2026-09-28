using Marshal.Abstractions.Configuration;
using Marshal.Abstractions.Skills;
using Marshal.Core.Configuration;
using Marshal.Core.Skills;
using Microsoft.Extensions.Logging.Abstractions;

namespace Marshal.Core.Tests.Skills;

public sealed class FileSkillCatalogTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("marshal-skills-").FullName;
    private ConfigDirectoryWatcher? _watcher;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Load_FrontmatterBodyAndFiles()
    {
        Write("rss-digest/SKILL.md", """
            ---
            name: rss-digest
            description: Summarizes unread RSS articles. Use for the morning briefing.
            license: MIT
            compatibility: Needs the rss tools
            metadata:
              author: lukas
            allowed-tools: rss_search rss_read
            ---
            # RSS digest

            1. Search unread articles.
            """);
        Write("rss-digest/references/format.md", "Most important first.");

        using FileSkillCatalog catalog = Load();

        Skill skill = Assert.Single(catalog.Skills);
        Assert.Equal("rss-digest", skill.Name);
        Assert.Equal("Summarizes unread RSS articles. Use for the morning briefing.", skill.Description);
        Assert.StartsWith("# RSS digest", skill.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("---", skill.Body, StringComparison.Ordinal);
        Assert.Equal("Most important first.", skill.Files["references/format.md"]);
        Assert.Same(skill, catalog.Find("rss-digest"));
        Assert.Empty(catalog.Errors);
    }

    [Theory]
    [InlineData("no-description", "---\nname: no-description\n---\nBody", "description")]
    [InlineData("mismatch", "---\nname: other\ndescription: d\n---\nBody", "directory")]
    [InlineData("Bad-Name", "---\nname: Bad-Name\ndescription: d\n---\nBody", "name")]
    [InlineData("double--hyphen", "---\nname: double--hyphen\ndescription: d\n---\nBody", "name")]
    [InlineData("no-frontmatter", "# Just markdown", "frontmatter")]
    [InlineData("unknown-key", "---\nname: unknown-key\ndescription: d\nmodel: opus\n---\nBody", "model")]
    public void Load_InvalidSkill_RejectedOthersLoad(string directory, string content, string expected)
    {
        Write("good/SKILL.md", "---\nname: good\ndescription: A good skill.\n---\nBody");
        Write($"{directory}/SKILL.md", content);

        using FileSkillCatalog catalog = Load();

        Assert.Equal(["good"], catalog.Skills.Select(skill => skill.Name));
        ConfigFileError error = Assert.Single(catalog.Errors);
        Assert.Equal($"{directory}/SKILL.md", error.File);
        Assert.Contains(expected, error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Load_DescriptionTooLong_Rejected()
    {
        Write("long/SKILL.md", $"---\nname: long\ndescription: {new string('x', 1025)}\n---\nBody");

        using FileSkillCatalog catalog = Load();

        Assert.Empty(catalog.Skills);
        Assert.Contains("1024", Assert.Single(catalog.Errors).Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("---\nname: leak\ndescription: d\ntoken: abc\n---\nBody")]
    [InlineData("---\nname: leak\ndescription: d\n---\nUse the key sk-ant-api03-abcdefghijklmnop to call it.")]
    public void Load_SecretInSkill_RejectedAsLiteralSecret(string content)
    {
        Write("leak/SKILL.md", content);

        using FileSkillCatalog catalog = Load();

        Assert.Empty(catalog.Skills);
        ConfigFileError error = Assert.Single(catalog.Errors);
        Assert.True(error.LiteralSecret);
        Assert.DoesNotContain("sk-ant-api03", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_SecretInExtraFile_Rejected()
    {
        Write("leak/SKILL.md", "---\nname: leak\ndescription: d\n---\nBody");
        Write("leak/config.yaml", "password: hunter2\n");

        using FileSkillCatalog catalog = Load();

        Assert.Empty(catalog.Skills);
        Assert.True(Assert.Single(catalog.Errors).LiteralSecret);
    }

    [Fact]
    public async Task SkillAddedOnDisk_ReloadsAndNotifies()
    {
        using FileSkillCatalog catalog = Load(watch: true);
        int changes = 0;
        using IDisposable subscription = catalog.OnChange(() => Interlocked.Increment(ref changes));

        Write("late/SKILL.md", "---\nname: late\ndescription: Added later.\n---\nBody");

        DateTime deadline = DateTime.UtcNow.AddSeconds(10);
        while (catalog.Skills.Count == 0 && DateTime.UtcNow < deadline)
            await Task.Delay(50, Ct);

        Assert.Equal(["late"], catalog.Skills.Select(skill => skill.Name));
        Assert.True(changes > 0);
    }

    [Fact]
    public void Load_MissingDirectory_NoSkills()
    {
        Directory.Delete(_directory, recursive: true);

        using FileSkillCatalog catalog = Load();

        Assert.Empty(catalog.Skills);
        Assert.Empty(catalog.Errors);
    }

    public void Dispose()
    {
        _watcher?.Dispose();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    private FileSkillCatalog Load(bool watch = false)
    {
        _watcher = watch ? new ConfigDirectoryWatcher(_directory) : null;
        return new FileSkillCatalog(_directory, _watcher, "**/*", new ConfigSecretScanner(), NullLogger<FileSkillCatalog>.Instance);
    }

    private void Write(string path, string content)
    {
        string full = Path.Combine(_directory, path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }
}
