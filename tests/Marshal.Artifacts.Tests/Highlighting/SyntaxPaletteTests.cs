using System.Text.RegularExpressions;
using Marshal.Artifacts.Highlighting;
using SkiaSharp;

namespace Marshal.Artifacts.Tests.Highlighting;

public sealed partial class SyntaxPaletteTests
{
    [Fact]
    public void DarkPalette_MatchesWebTheme()
    {
        string css = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Highlighting", "tokens.css"));
        int start = css.IndexOf("[data-theme='dark'] {", StringComparison.Ordinal);
        string dark = css[start..css.IndexOf('}', start)];
        var web = SyntaxVariable().Matches(dark).ToDictionary(match => match.Groups[1].Value, match => SKColor.Parse(match.Groups[2].Value));

        var expected = SyntaxPalette.Dark.ToDictionary(pair => CodeTokens.Name(pair.Key), pair => pair.Value);
        expected["guide"] = SyntaxPalette.Guide;

        Assert.Equal(expected.OrderBy(pair => pair.Key, StringComparer.Ordinal), web.OrderBy(pair => pair.Key, StringComparer.Ordinal));
    }

    [GeneratedRegex(@"--syntax-([a-z0-9-]+):\s*(#[0-9A-Fa-f]{6});")]
    private static partial Regex SyntaxVariable();
}
