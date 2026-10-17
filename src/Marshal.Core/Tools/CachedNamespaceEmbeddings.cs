using System.Collections.Concurrent;
using Marshal.Abstractions.Tools;
using Microsoft.Extensions.AI;

namespace Marshal.Core.Tools;

/// <summary>
/// Caches description embeddings by embedding model and description text, so a tool source reload re-embeds only
/// descriptions that changed. Descriptions are short and few, so the cache holds them all.
/// </summary>
public sealed class CachedNamespaceEmbeddings(IEmbeddingGenerator<string, Embedding<float>> embeddings) : INamespaceEmbeddings
{
    private readonly ConcurrentDictionary<(string Model, string Description), ReadOnlyMemory<float>> _cache = new();
    private readonly string _model = embeddings.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId ?? "default";

    public async Task<IReadOnlyDictionary<string, ReadOnlyMemory<float>>> GetAsync(IReadOnlyList<ToolNamespace> namespaces, CancellationToken cancellationToken)
    {
        string[] missing = [.. namespaces.Select(item => item.Description).Where(description => !_cache.ContainsKey((_model, description))).Distinct(StringComparer.Ordinal)];
        if (missing.Length > 0)
        {
            GeneratedEmbeddings<Embedding<float>> generated = await embeddings.GenerateAsync(missing, cancellationToken: cancellationToken);
            for (int i = 0; i < missing.Length; i++)
                _cache[(_model, missing[i])] = generated[i].Vector.ToArray();
        }

        return namespaces.ToDictionary(item => item.Name, item => _cache[(_model, item.Description)], StringComparer.Ordinal);
    }
}
