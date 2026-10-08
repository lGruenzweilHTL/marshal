
namespace Marshal.Abstractions.Tools;

/// <summary>
/// Glob matching on internal tool ids (and on tool names as an MCP server reports them): <c>*</c> matches any run of
/// characters, <c>?</c> one character. Case-sensitive, like the ids.
/// </summary>
public static class ToolGlob
{
    /// <summary>True when <paramref name="id"/> matches <paramref name="pattern"/>.</summary>
    public static bool Matches(string pattern, ToolId id) => Matches(pattern.AsSpan(), id.ToString().AsSpan());

    /// <summary>True when <paramref name="name"/> (e.g. a tool name reported by an MCP server) matches <paramref name="pattern"/>.</summary>
    public static bool Matches(string pattern, string name) => Matches(pattern.AsSpan(), name.AsSpan());

    private static bool Matches(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text)
    {
        int p = 0;
        int t = 0;
        int star = -1;
        int resume = 0;
        while (t < text.Length)
        {
            if (p < pattern.Length && (pattern[p] == '?' || pattern[p] == text[t]))
            {
                p++;
                t++;
            }
            else if (p < pattern.Length && pattern[p] == '*')
            {
                star = p++;
                resume = t;
            }
            else if (star >= 0)
            {
                p = star + 1;
                t = ++resume;
            }
            else
            {
                return false;
            }
        }

        while (p < pattern.Length && pattern[p] == '*')
            p++;

        return p == pattern.Length;
    }
}
