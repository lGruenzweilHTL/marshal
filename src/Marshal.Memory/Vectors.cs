using System.Runtime.InteropServices;

namespace Marshal.Memory;

/// <summary>Small vector helpers for embeddings (float32).</summary>
public static class Vectors
{
    /// <summary>Cosine similarity; 0 when either vector is zero or the lengths differ.</summary>
    public static double Cosine(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        if (a.Length != b.Length || a.Length == 0)
            return 0;

        double dot = 0, normA = 0, normB = 0;
        for (int index = 0; index < a.Length; index++)
        {
            dot += a[index] * b[index];
            normA += a[index] * a[index];
            normB += b[index] * b[index];
        }

        return normA == 0 || normB == 0 ? 0 : dot / Math.Sqrt(normA * normB);
    }

    /// <summary>The normalized mean of equally long vectors, or null when there are none.</summary>
    public static float[]? NormalizedMean(IReadOnlyList<float[]> vectors)
    {
        if (vectors.Count == 0)
            return null;

        float[] mean = new float[vectors[0].Length];
        foreach (float[] vector in vectors.Where(vector => vector.Length == mean.Length))
        {
            float length = MathF.Sqrt(vector.Sum(value => value * value));
            for (int index = 0; index < mean.Length && length > 0; index++)
                mean[index] += vector[index] / length;
        }

        float total = MathF.Sqrt(mean.Sum(value => value * value));
        return total == 0 ? mean : [.. mean.Select(value => value / total)];
    }

    /// <summary>float32 little-endian bytes.</summary>
    public static byte[] ToBlob(ReadOnlySpan<float> vector) => MemoryMarshal.AsBytes(vector).ToArray();

    /// <summary>Reads float32 little-endian bytes.</summary>
    public static float[] FromBlob(byte[] blob) => MemoryMarshal.Cast<byte, float>(blob).ToArray();
}
