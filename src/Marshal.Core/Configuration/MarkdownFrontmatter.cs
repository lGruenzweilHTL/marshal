using System.Text.RegularExpressions;

namespace Marshal.Core.Configuration;

/// <summary>
/// Splits a Markdown file with YAML frontmatter (<c>---</c> lines) into the YAML and the body.
/// </summary>
public static partial class MarkdownFrontmatter
{
    /// <summary>The frontmatter YAML and the body; null when the text has no frontmatter.</summary>
    public static (string Yaml, string Body)? Split(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal);
        Match match = Pattern().Match(text);
        return match.Success ? (match.Groups[1].Value, text[match.Length..].TrimStart('\n')) : null;
    }

    [GeneratedRegex(@"\A---\n(.*?)\n?---[ \t]*(?:\n|\z)", RegexOptions.Singleline | RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}
