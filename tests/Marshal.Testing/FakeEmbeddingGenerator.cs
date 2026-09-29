using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.AI;

namespace Marshal.Testing;

/// <summary>
/// Deterministic <see cref="IEmbeddingGenerator{TInput,TEmbedding}"/>: the same text always gives the same
/// unit vector. Specific vectors can be pinned per text for similarity tests. With a <paramref name="modelId"/> it
/// reports <see cref="EmbeddingGeneratorMetadata"/> for that model.
/// </summary>
/// <param name="dimensions">Length of generated vectors.</param>
/// <param name="modelId">Model id reported in the metadata; none when null.</param>
public sealed class FakeEmbeddingGenerator(int dimensions = 16, string? modelId = null) : IEmbeddingGenerator<string, Embedding<float>>
{
    private readonly Dictionary<string, float[]> _pinned = new(StringComparer.Ordinal);

    /// <summary>Number of texts embedded so far.</summary>
    public int CallCount { get; private set; }

    /// <summary>Makes <paramref name="text"/> embed to <paramref name="vector"/> (normalized).</summary>
    public FakeEmbeddingGenerator Pin(string text, params float[] vector)
    {
        _pinned[text] = Normalize(vector);
        return this;
    }

    /// <inheritdoc />
    public Task<GeneratedEmbeddings<Embedding<float>>> GenerateAsync(
        IEnumerable<string> values, EmbeddingGenerationOptions? options = null, CancellationToken cancellationToken = default)
    {
        var embeddings = new GeneratedEmbeddings<Embedding<float>>();
        foreach (string value in values)
        {
            CallCount++;
            embeddings.Add(new Embedding<float>(_pinned.TryGetValue(value, out float[]? pinned) ? pinned : HashVector(value)));
        }

        return Task.FromResult(embeddings);
    }

    /// <inheritdoc />
    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceKey is not null ? null
        : serviceType == typeof(EmbeddingGeneratorMetadata) && modelId is not null ? new EmbeddingGeneratorMetadata("fake", null, modelId, dimensions)
        : serviceType.IsInstanceOfType(this) ? this
        : null;

    /// <inheritdoc />
    public void Dispose()
    {
    }

    private float[] HashVector(string text)
    {
        float[] vector = new float[dimensions];
        int block = 0;
        int filled = 0;
        while (filled < dimensions)
        {
            byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes($"{block++}:{text}"));
            for (int i = 0; i + 1 < hash.Length && filled < dimensions; i += 2)
                vector[filled++] = (BitConverter.ToUInt16(hash, i) / (float)ushort.MaxValue) - 0.5f;
        }

        return Normalize(vector);
    }

    private static float[] Normalize(float[] vector)
    {
        float length = MathF.Sqrt(vector.Sum(value => value * value));
        return length == 0 ? vector : vector.Select(value => value / length).ToArray();
    }
}
