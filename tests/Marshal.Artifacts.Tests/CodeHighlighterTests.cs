using static Marshal.Artifacts.CodeHighlighter;

namespace Marshal.Artifacts.Tests;

public sealed class CodeHighlighterTests
{
    [Fact]
    public void CSharp_KeywordStringAndComment()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("Program.cs", "var name = \"Marshal\"; // greeting");

        Assert.Equal([new(0, 3, CodeToken.Keyword), new(11, 9, CodeToken.String), new(22, 11, CodeToken.Comment)], spans[0]);
    }

    [Fact]
    public void TypeScript_Number()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("app.ts", "let x2 = 42;");

        Assert.Equal([new(0, 3, CodeToken.Keyword), new(9, 2, CodeToken.Number)], spans[0]);
    }

    [Fact]
    public void BlockComment_SpansLines()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("app.ts", "a /* one", "two", "end */ b");

        Assert.Equal([new(2, 6, CodeToken.Comment)], spans[0]);
        Assert.Equal([new(0, 3, CodeToken.Comment)], spans[1]);
        Assert.Equal([new(0, 6, CodeToken.Comment)], spans[2]);
    }

    [Fact]
    public void TemplateLiteral_SpansLines()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("app.ts", "a = `x", "y`");

        Assert.Equal([new(4, 2, CodeToken.String)], spans[0]);
        Assert.Equal([new(0, 2, CodeToken.String)], spans[1]);
    }

    [Fact]
    public void String_EscapedQuote_StaysOpen()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("Program.cs", "s = \"a\\\"b\" + c");

        Assert.Equal([new(4, 6, CodeToken.String)], spans[0]);
    }

    [Fact]
    public void String_Unterminated_EndsWithLine()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("Program.cs", "x = \"abc", "y");

        Assert.Equal([new(4, 4, CodeToken.String)], spans[0]);
        Assert.Empty(spans[1]);
    }

    [Fact]
    public void Python_HashComment()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("tool.py", "x = 1  # note");

        Assert.Equal([new(4, 1, CodeToken.Number), new(7, 6, CodeToken.Comment)], spans[0]);
    }

    [Fact]
    public void Bash_HashInsideWord_IsNoComment()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("run.sh", "echo ${#x}");

        Assert.Equal([new(0, 4, CodeToken.Keyword)], spans[0]);
    }

    [Fact]
    public void Yaml_ApostropheInText_IsNoString()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("marshal.yaml", "description: Marshal's agent # hi");

        Assert.Equal([new(29, 4, CodeToken.Comment)], spans[0]);
    }

    [Fact]
    public void Sql_KeywordsIgnoreCase()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("query.sql", "select * FROM t");

        Assert.Equal([new(0, 6, CodeToken.Keyword), new(9, 4, CodeToken.Keyword)], spans[0]);
    }

    [Fact]
    public void Dockerfile_ByName()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("Dockerfile", "FROM alpine");

        Assert.Equal([new(0, 4, CodeToken.Keyword)], spans[0]);
    }

    [Fact]
    public void GCode_CommandsAndComments()
    {
        IReadOnlyList<IReadOnlyList<CodeSpan>> spans = Highlight("part.gcode", "G1 X10.5 ; move");

        Assert.Equal([new(0, 2, CodeToken.Keyword), new(9, 6, CodeToken.Comment)], spans[0]);
    }

    [Theory]
    [InlineData("notes.txt")]
    [InlineData("Makefile")]
    public void UnknownLanguage_IsPlain(string name) => Assert.Null(LanguageOf(name));

    private static IReadOnlyList<IReadOnlyList<CodeSpan>> Highlight(string name, params string[] lines) =>
        CodeHighlighter.Highlight(LanguageOf(name) ?? throw new InvalidOperationException($"No language for {name}."), lines);
}
