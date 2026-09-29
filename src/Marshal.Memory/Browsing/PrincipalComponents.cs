namespace Marshal.Memory.Browsing;

/// <summary>
/// Projects embeddings onto their first two principal components (power iteration on the centered data, so the
/// covariance matrix is never built). Deterministic: the same vectors always give the same points.
/// </summary>
public static class PrincipalComponents
{
    private const int Iterations = 100;

    public static IReadOnlyList<(double X, double Y)> Project(IReadOnlyList<float[]> vectors)
    {
        if (vectors.Count == 0)
            return [];

        int dimensions = vectors[0].Length;
        double[] mean = new double[dimensions];
        foreach (float[] vector in vectors)
        {
            for (int i = 0; i < dimensions; i++)
                mean[i] += vector[i] / (double)vectors.Count;
        }

        double[][] centered = [.. vectors.Select(vector => vector.Select((value, i) => value - mean[i]).ToArray())];
        double[] first = Component(centered, null);
        double[] second = Component(centered, first);
        return [.. centered.Select(row => (Dot(row, first), Dot(row, second)))];
    }

    // The dominant direction of the data, orthogonal to `previous` when given; zero when there is no spread.
    private static double[] Component(double[][] rows, double[]? previous)
    {
        int dimensions = rows[0].Length;
        double[] direction = [.. Enumerable.Range(0, dimensions).Select(i => 1.0 + (i * 0.01))];
        for (int iteration = 0; iteration < Iterations; iteration++)
        {
            if (previous is not null)
                Subtract(direction, previous, Dot(direction, previous));

            double[] next = new double[dimensions];
            foreach (double[] row in rows)
            {
                double weight = Dot(row, direction);
                for (int i = 0; i < dimensions; i++)
                    next[i] += weight * row[i];
            }

            double norm = Math.Sqrt(Dot(next, next));
            if (norm < 1e-12)
                return new double[dimensions];

            direction = [.. next.Select(value => value / norm)];
        }

        if (previous is not null)
            Subtract(direction, previous, Dot(direction, previous));

        // A fixed sign: the largest component is positive.
        int largest = Array.IndexOf(direction, direction.MaxBy(Math.Abs));
        return direction[largest] < 0 ? [.. direction.Select(value => -value)] : direction;
    }

    private static void Subtract(double[] target, double[] direction, double amount)
    {
        for (int i = 0; i < target.Length; i++)
            target[i] -= amount * direction[i];
    }

    private static double Dot(double[] a, double[] b)
    {
        double sum = 0;
        for (int i = 0; i < a.Length; i++)
            sum += a[i] * b[i];

        return sum;
    }
}
