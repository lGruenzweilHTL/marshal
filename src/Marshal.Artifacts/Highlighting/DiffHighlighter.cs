namespace Marshal.Artifacts.Highlighting;

internal enum DiffRowKind
{
    Meta,
    Hunk,
    Added,
    Removed,
    Context,
}

/// <summary>One diff line: its kind and the highlight spans of the code after the marker (offset by the marker).</summary>
internal sealed record DiffRow(DiffRowKind Kind, IReadOnlyList<CodeSpan> Spans);

/// <summary>
/// Unified diffs with the code highlighted in each file's language (from <c>+++ b/path</c> or <c>diff --git</c>).
/// The old side (removed and context lines) and the new side (added and context lines) keep separate states.
/// </summary>
internal static class DiffHighlighter
{
    public static IReadOnlyList<DiffRow> Highlight(IReadOnlyList<string> lines)
    {
        var rows = new List<DiffRow>(lines.Count);
        CodeLanguage? language = null;
        var oldSide = new HighlightState();
        var newSide = new HighlightState();
        bool inHunk = false;

        for (int index = 0; index < lines.Count; index++)
        {
            string line = lines[index];
            bool fileHeader = line.StartsWith("diff ", StringComparison.Ordinal)
                || (line.StartsWith("--- ", StringComparison.Ordinal) && index + 1 < lines.Count && lines[index + 1].StartsWith("+++ ", StringComparison.Ordinal));
            if (fileHeader || !inHunk && !line.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = false;
                if (Path(line) is { } path)
                {
                    language = CodeLanguages.ForFile(path);
                    oldSide = new HighlightState();
                    newSide = new HighlightState();
                }

                rows.Add(new DiffRow(DiffRowKind.Meta, []));
                continue;
            }

            if (line.StartsWith("@@", StringComparison.Ordinal))
            {
                inHunk = true;
                rows.Add(new DiffRow(DiffRowKind.Hunk, []));
                continue;
            }

            if (line.StartsWith('\\'))
            {
                rows.Add(new DiffRow(DiffRowKind.Meta, []));
                continue;
            }

            string code = line.Length > 0 ? line[1..] : "";
            DiffRowKind kind = line.StartsWith('+') ? DiffRowKind.Added : line.StartsWith('-') ? DiffRowKind.Removed : DiffRowKind.Context;
            IReadOnlyList<CodeSpan> spans = [];
            if (language is not null)
            {
                if (kind != DiffRowKind.Added)
                    spans = CodeHighlighter.HighlightLine(language, code, oldSide);
                if (kind != DiffRowKind.Removed)
                    spans = CodeHighlighter.HighlightLine(language, code, newSide);
            }

            rows.Add(new DiffRow(kind, spans.Select(span => span with { Start = span.Start + 1 }).ToList()));
        }

        return rows;
    }

    /// <summary>The new file's path from a <c>+++</c> header or a <c>diff --git</c> line; <see langword="null"/> otherwise.</summary>
    private static string? Path(string line)
    {
        if (line.StartsWith("+++ ", StringComparison.Ordinal))
        {
            string path = line[4..].Split('\t')[0].Trim();
            if (path == "/dev/null")
                return null;
            return path.StartsWith("b/", StringComparison.Ordinal) ? path[2..] : path;
        }

        if (line.StartsWith("diff --git ", StringComparison.Ordinal))
        {
            int separator = line.LastIndexOf(" b/", StringComparison.Ordinal);
            return separator < 0 ? null : line[(separator + 3)..];
        }

        return null;
    }
}
