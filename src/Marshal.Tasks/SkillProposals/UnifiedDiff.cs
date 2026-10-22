using System.Globalization;
using System.Text;

namespace Marshal.Tasks.SkillProposals;

/// <summary>
/// A line-based unified diff (longest common subsequence, three lines of context), in the same format as the diffs of
/// coding proposals, so the web UI's diff view shows it.
/// </summary>
public static class UnifiedDiff
{
    private const int Context = 3;

    /// <summary>The diff from <paramref name="before"/> to <paramref name="after"/>; empty when they are equal.</summary>
    public static string Create(string before, string after, string path)
    {
        string[] left = Lines(before);
        string[] right = Lines(after);
        List<(char Kind, string Line)> edits = Edits(left, right);
        if (edits.All(edit => edit.Kind == ' '))
            return "";

        var diff = new StringBuilder();
        diff.Append(CultureInfo.InvariantCulture, $"--- a/{path}\n+++ b/{path}\n");
        foreach ((int start, int end) in Hunks(edits))
        {
            int leftStart = 1 + edits.Take(start).Count(edit => edit.Kind != '+');
            int rightStart = 1 + edits.Take(start).Count(edit => edit.Kind != '-');
            int leftCount = edits.Skip(start).Take(end - start).Count(edit => edit.Kind != '+');
            int rightCount = edits.Skip(start).Take(end - start).Count(edit => edit.Kind != '-');
            diff.Append(CultureInfo.InvariantCulture, $"@@ -{(leftCount == 0 ? leftStart - 1 : leftStart)},{leftCount} +{(rightCount == 0 ? rightStart - 1 : rightStart)},{rightCount} @@\n");
            for (int index = start; index < end; index++)
                diff.Append(edits[index].Kind).Append(edits[index].Line).Append('\n');
        }

        return diff.ToString();
    }

    private static string[] Lines(string text) =>
        text.Length == 0 ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).TrimEnd('\n').Split('\n');

    private static List<(char, string)> Edits(string[] left, string[] right)
    {
        int[,] common = new int[left.Length + 1, right.Length + 1];
        for (int i = left.Length - 1; i >= 0; i--)
        {
            for (int j = right.Length - 1; j >= 0; j--)
                common[i, j] = left[i] == right[j] ? common[i + 1, j + 1] + 1 : Math.Max(common[i + 1, j], common[i, j + 1]);
        }

        var edits = new List<(char, string)>();
        int a = 0, b = 0;
        while (a < left.Length && b < right.Length)
        {
            if (left[a] == right[b])
            {
                edits.Add((' ', left[a]));
                a++;
                b++;
            }
            else if (common[a + 1, b] >= common[a, b + 1])
            {
                edits.Add(('-', left[a++]));
            }
            else
            {
                edits.Add(('+', right[b++]));
            }
        }

        while (a < left.Length)
            edits.Add(('-', left[a++]));
        while (b < right.Length)
            edits.Add(('+', right[b++]));
        return edits;
    }

    // Ranges of edits around changes, merged when their context overlaps.
    private static List<(int Start, int End)> Hunks(List<(char Kind, string Line)> edits)
    {
        var hunks = new List<(int Start, int End)>();
        for (int index = 0; index < edits.Count; index++)
        {
            if (edits[index].Kind == ' ')
                continue;

            int start = Math.Max(0, index - Context);
            int end = Math.Min(edits.Count, index + Context + 1);
            if (hunks.Count > 0 && start <= hunks[^1].End)
                hunks[^1] = (hunks[^1].Start, Math.Max(hunks[^1].End, end));
            else
                hunks.Add((start, end));
        }

        return hunks;
    }
}
