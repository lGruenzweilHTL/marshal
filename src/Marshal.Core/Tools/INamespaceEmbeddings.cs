using Marshal.Abstractions.Tools;

namespace Marshal.Core.Tools;

/// <summary>
/// Embeddings of namespace descriptions for tool retrieval, computed with the same generator as the turn embedding.
/// </summary>
public interface INamespaceEmbeddings
{
    /// <summary>One embedding per namespace name.</summary>
    Task<IReadOnlyDictionary<string, ReadOnlyMemory<float>>> GetAsync(IReadOnlyList<ToolNamespace> namespaces, CancellationToken cancellationToken);
}
