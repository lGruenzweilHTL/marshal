using Microsoft.Extensions.AI;

namespace Marshal.Core.Models;

/// <summary>
/// Creates the embedding generator. Implemented by the host, the only project that references provider SDKs.
/// </summary>
public interface IEmbeddingGeneratorFactory
{
    IEmbeddingGenerator<string, Embedding<float>> Create(EmbeddingModelOptions options);
}
