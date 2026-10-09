using SkiaSharp;

namespace Marshal.Artifacts.Highlighting;

/// <summary>
/// Token colors of display pages: the dark theme's <c>--syntax-*</c> values from <c>web/src/ui/tokens.css</c>
/// (a test compares them), so code looks the same on the TV and in the browser.
/// </summary>
internal static class SyntaxPalette
{
    public static IReadOnlyDictionary<CodeToken, SKColor> Dark { get; } = new Dictionary<CodeToken, SKColor>
    {
        [CodeToken.Keyword] = SKColor.Parse("#FFA061"),
        [CodeToken.Type] = SKColor.Parse("#5FD4C4"),
        [CodeToken.Function] = SKColor.Parse("#82AEFF"),
        [CodeToken.String] = SKColor.Parse("#55C98F"),
        [CodeToken.Number] = SKColor.Parse("#E7C46B"),
        [CodeToken.Literal] = SKColor.Parse("#E7C46B"),
        [CodeToken.Comment] = SKColor.Parse("#8E979D"),
        [CodeToken.Property] = SKColor.Parse("#B9A7F8"),
        [CodeToken.Meta] = SKColor.Parse("#D98FD0"),
        [CodeToken.Tag] = SKColor.Parse("#FF8A80"),
        [CodeToken.Variable] = SKColor.Parse("#B9A7F8"),
        [CodeToken.Bracket1] = SKColor.Parse("#E7C46B"),
        [CodeToken.Bracket2] = SKColor.Parse("#D98FD0"),
        [CodeToken.Bracket3] = SKColor.Parse("#82AEFF"),
        [CodeToken.BracketUnmatched] = SKColor.Parse("#F06C6C"),
    };

    /// <summary>Indent guide lines (<c>--syntax-guide</c>).</summary>
    public static SKColor Guide { get; } = SKColor.Parse("#3A4046");
}
