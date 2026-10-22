using System.Security.Cryptography;
using System.Text;

namespace Marshal.Tasks.SkillProposals;

/// <summary>Runs that repeat one workflow: the same tool sequence and similar requests.</summary>
/// <param name="SequenceHash">Hash of the tool id sequence (one proposal per sequence and owner).</param>
/// <param name="Runs">The runs, oldest first.</param>
public sealed record WorkflowGroup(string SequenceHash, IReadOnlyList<WorkflowRun> Runs)
{
    public IReadOnlyList<string> Tools => Runs[0].Tools;
}

/// <summary>
/// Groups runs whose tool id sequences are equal, then splits each group by input similarity: a run joins the first
/// cluster whose every member it is at least <c>threshold</c> cosine-similar to. Only clusters of at least
/// <c>minRuns</c> are kept.
/// </summary>
public static class WorkflowGrouper
{
    public const double DefaultThreshold = 0.8;

    public const int DefaultMinRuns = 3;

    public static IReadOnlyList<WorkflowGroup> Group(IEnumerable<WorkflowRun> runs, double threshold = DefaultThreshold, int minRuns = DefaultMinRuns)
    {
        var groups = new List<WorkflowGroup>();
        foreach (IGrouping<string, WorkflowRun> sequence in runs.Where(run => run.Embedding is not null).GroupBy(run => string.Join('\n', run.Tools), StringComparer.Ordinal))
        {
            var clusters = new List<List<WorkflowRun>>();
            foreach (WorkflowRun run in sequence)
            {
                List<WorkflowRun>? cluster = clusters.FirstOrDefault(members => members.All(member => Similarity(member.Embedding!, run.Embedding!) >= threshold));
                if (cluster is null)
                    clusters.Add([run]);
                else
                    cluster.Add(run);
            }

            string hash = Hash(sequence.Key);
            groups.AddRange(clusters.Where(cluster => cluster.Count >= minRuns).Select(cluster => new WorkflowGroup(hash, cluster)));
        }

        return groups;
    }

    public static string Hash(string sequence) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(sequence)));

    private static double Similarity(float[] left, float[] right)
    {
        if (left.Length != right.Length)
            return 0;

        double dot = 0, leftNorm = 0, rightNorm = 0;
        for (int index = 0; index < left.Length; index++)
        {
            dot += left[index] * right[index];
            leftNorm += left[index] * left[index];
            rightNorm += right[index] * right[index];
        }

        return leftNorm == 0 || rightNorm == 0 ? 0 : dot / Math.Sqrt(leftNorm * rightNorm);
    }
}
