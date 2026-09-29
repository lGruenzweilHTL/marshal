using Marshal.Integrations.Notes.Index;

namespace Marshal.Integrations.Notes.Tests.Index;

public sealed class NoteSectionsTests
{
    [Fact]
    public void Split_ByHeadingsOutsideCodeFences()
    {
        const string note = "Intro line\n\n# Marshal\nText.\n## Setup\n```bash\n# not a heading\n```\n### Details\n\n";

        IReadOnlyList<NoteSection> sections = NoteSections.Split("projects/marshal.md", note);

        Assert.Equal(
            [("marshal", "Intro line"), ("Marshal", "Text."), ("Setup", "```bash\n# not a heading\n```")],
            sections.Select(section => (section.Heading, section.Text)));
    }

    [Fact]
    public void Split_LongSectionIntoParagraphPieces()
    {
        string paragraph = new('a', 900);
        string note = $"# Long\n{paragraph}\n\n{paragraph}\n\n{paragraph}";

        IReadOnlyList<NoteSection> sections = NoteSections.Split("long.md", note);

        Assert.True(sections.Count >= 2);
        Assert.All(sections, section => Assert.Equal("Long", section.Heading));
        Assert.All(sections, section => Assert.True(section.Text.Length <= NoteSections.MaxSectionLength));
    }

    [Fact]
    public void Split_EmptyNote_NoSections()
    {
        Assert.Empty(NoteSections.Split("empty.md", "\n\n"));
    }
}
