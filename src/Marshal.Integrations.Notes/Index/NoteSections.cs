using System.Text;

namespace Marshal.Integrations.Notes.Index;

/// <summary>A heading and the text below it.</summary>
internal sealed record NoteSection(string Heading, string Text);

/// <summary>
/// Splits Markdown into sections at ATX headings (<c>#</c> to <c>######</c>) outside code fences. Text before the
/// first heading is filed under the file name; sections longer than <see cref="MaxSectionLength"/> are cut at
/// paragraph boundaries (or hard, for a single huge paragraph) into pieces with the same heading.
/// </summary>
internal static class NoteSections
{
    public const int MaxSectionLength = 1500;

    public static IReadOnlyList<NoteSection> Split(string path, string markdown)
    {
        var sections = new List<NoteSection>();
        string heading = System.IO.Path.GetFileNameWithoutExtension(path);
        var text = new StringBuilder();
        bool inFence = false;
        foreach (string line in markdown.ReplaceLineEndings("\n").Split('\n'))
        {
            string trimmed = line.TrimStart();
            if (trimmed.StartsWith("```", StringComparison.Ordinal) || trimmed.StartsWith("~~~", StringComparison.Ordinal))
                inFence = !inFence;

            if (!inFence && Heading(trimmed) is { } next)
            {
                Add(sections, heading, text.ToString());
                heading = next;
                text.Clear();
                continue;
            }

            text.Append(line).Append('\n');
        }

        Add(sections, heading, text.ToString());
        return sections;
    }

    private static string? Heading(string line)
    {
        int level = 0;
        while (level < line.Length && line[level] == '#')
            level++;

        return level is >= 1 and <= 6 && line.Length > level && line[level] == ' ' && line[level..].Trim().TrimEnd('#').Trim() is { Length: > 0 } title
            ? title
            : null;
    }

    private static void Add(List<NoteSection> sections, string heading, string text)
    {
        string body = text.Trim();
        if (body.Length == 0)
            return;

        var piece = new StringBuilder();
        foreach (string paragraph in body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (piece.Length > 0 && piece.Length + 2 + paragraph.Length > MaxSectionLength)
            {
                sections.Add(new NoteSection(heading, piece.ToString()));
                piece.Clear();
            }

            string rest = paragraph;
            while (rest.Length > MaxSectionLength)
            {
                sections.Add(new NoteSection(heading, rest[..MaxSectionLength]));
                rest = rest[MaxSectionLength..];
            }

            if (piece.Length > 0)
                piece.Append("\n\n");
            piece.Append(rest);
        }

        if (piece.Length > 0)
            sections.Add(new NoteSection(heading, piece.ToString()));
    }
}
