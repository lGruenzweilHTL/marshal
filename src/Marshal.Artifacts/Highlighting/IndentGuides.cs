namespace Marshal.Artifacts.Highlighting;

/// <summary>Indent guides: the file's indent unit and the number of guides per line (spec code-highlighting).</summary>
internal static class IndentGuides
{
    public static (int Unit, int[] Guides) Compute(IReadOnlyList<string> lines)
    {
        int?[] indents = lines.Select(Indent).ToArray();

        var steps = new Dictionary<int, int>();
        int? previous = null;
        foreach (int? indent in indents)
        {
            if (indent is not { } current)
                continue;
            if (previous is { } before && current > before)
                steps[current - before] = steps.GetValueOrDefault(current - before) + 1;
            previous = current;
        }

        int unit = steps.Count == 0 ? 4 : Math.Clamp(steps.OrderByDescending(step => step.Value).ThenBy(step => step.Key).First().Key, 2, 8);
        int[] guides = indents.Select(indent => indent is { } value ? (value + unit - 1) / unit : -1).ToArray();

        for (int index = 0; index < guides.Length; index++)
        {
            if (guides[index] >= 0)
                continue;
            int before = index - 1;
            while (before >= 0 && indents[before] is null)
                before--;
            int after = index + 1;
            while (after < guides.Length && indents[after] is null)
                after++;
            guides[index] = Math.Min(before >= 0 ? guides[before] : 0, after < guides.Length ? guides[after] : 0);
        }

        return (unit, guides);
    }

    /// <summary>Leading spaces (a tab counts four), or <see langword="null"/> for a blank line.</summary>
    private static int? Indent(string line)
    {
        int indent = 0;
        foreach (char character in line)
        {
            if (character == ' ')
                indent++;
            else if (character == '\t')
                indent += 4;
            else
                return indent;
        }

        return null;
    }
}
