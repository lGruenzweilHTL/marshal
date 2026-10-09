using System.Text.Json;
using Marshal.Artifacts.Highlighting;

namespace Marshal.Artifacts.Tests.Highlighting;

/// <summary>Runs the parity cases shared with the web highlighter (<c>web/src/artifacts/highlighting/cases.json</c>).</summary>
public sealed class CodeHighlighterTests
{
    private static readonly JsonElement _cases = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Highlighting", "cases.json"))).RootElement;

    public static TheoryData<string> HighlightCases => Names("highlight");

    public static TheoryData<string> GuideCases => Names("guides");

    public static TheoryData<string> DiffCases => Names("diff");

    [Fact]
    public void LanguageByFileName()
    {
        foreach (JsonElement pair in _cases.GetProperty("languages").EnumerateArray())
            Assert.Equal(pair[1].GetString(), CodeLanguages.ForFile(pair[0].GetString()!)?.Id);
    }

    [Fact]
    public void LanguageByAlias()
    {
        foreach (JsonElement pair in _cases.GetProperty("aliases").EnumerateArray())
            Assert.Equal(pair[1].GetString(), CodeLanguages.ForAlias(pair[0].GetString()!)?.Id);
    }

    [Theory]
    [MemberData(nameof(HighlightCases))]
    public void Highlight(string name)
    {
        JsonElement test = Case("highlight", name);
        CodeLanguage language = CodeLanguages.ForFile(test.GetProperty("file").GetString()!) ?? throw new InvalidOperationException("No language.");
        string[] lines = test.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!).ToArray();

        IReadOnlyList<IReadOnlyList<CodeSpan>> actual = CodeHighlighter.Highlight(language, lines);

        string[] expected = test.GetProperty("spans").EnumerateArray().Select(Format).ToArray();
        Assert.Equal(expected, actual.Select(Format).ToArray());
    }

    [Theory]
    [MemberData(nameof(GuideCases))]
    public void Guides(string name)
    {
        JsonElement test = Case("guides", name);
        string[] lines = test.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!).ToArray();

        (int unit, int[] guides) = IndentGuides.Compute(lines);

        Assert.Equal(test.GetProperty("unit").GetInt32(), unit);
        Assert.Equal(test.GetProperty("guides").EnumerateArray().Select(guide => guide.GetInt32()), guides);
    }

    [Theory]
    [MemberData(nameof(DiffCases))]
    public void Diff(string name)
    {
        JsonElement test = Case("diff", name);
        string[] lines = test.GetProperty("lines").EnumerateArray().Select(line => line.GetString()!).ToArray();

        IReadOnlyList<DiffRow> rows = DiffHighlighter.Highlight(lines);

        string[] expected = test.GetProperty("rows").EnumerateArray()
            .Select(row => $"{row.GetProperty("kind").GetString()} {Format(row.GetProperty("spans"))}").ToArray();
        Assert.Equal(expected, rows.Select(row => $"{row.Kind.ToString().ToLowerInvariant()} {Format(row.Spans)}").ToArray());
    }

    [Fact]
    public void Languages_AllLoad() => Assert.True(CodeLanguages.All.Count >= 25);

    private static TheoryData<string> Names(string section) =>
        new(_cases.GetProperty(section).EnumerateArray().Select(test => test.GetProperty("name").GetString()!));

    private static JsonElement Case(string section, string name) =>
        _cases.GetProperty(section).EnumerateArray().Single(test => test.GetProperty("name").GetString() == name);

    private static string Format(JsonElement spans) =>
        string.Join(" ", spans.EnumerateArray().Select(span => $"[{span[0].GetInt32()},{span[1].GetInt32()},{span[2].GetString()}]"));

    private static string Format(IReadOnlyList<CodeSpan> spans) =>
        string.Join(" ", spans.Select(span => $"[{span.Start},{span.Length},{CodeTokens.Name(span.Token)}]"));
}
