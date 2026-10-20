using System.ClientModel;
using Marshal.Core.Models;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace Marshal.Api.Models;

/// <summary>
/// Creates the embedding generator for the configured provider.
/// </summary>
internal sealed class ProviderEmbeddingGeneratorFactory : IEmbeddingGeneratorFactory
{
    private static readonly Uri _defaultOllama = new("http://localhost:11434");

    public IEmbeddingGenerator<string, Embedding<float>> Create(EmbeddingModelOptions options) => ModelTelemetry.Instrument(CreateProvider(options));

    private static IEmbeddingGenerator<string, Embedding<float>> CreateProvider(EmbeddingModelOptions options) => options.Provider switch
    {
        ModelProviders.Ollama => new OllamaApiClient(options.Endpoint ?? _defaultOllama, options.Model),
        ModelProviders.OpenAiCompatible => new OpenAIClient(
                new ApiKeyCredential(options.ApiKey ?? "none"),
                new OpenAIClientOptions { Endpoint = options.Endpoint })
            .GetEmbeddingClient(options.Model)
            .AsIEmbeddingGenerator(),
        _ => throw new InvalidOperationException($"embedding.provider '{options.Provider}' is not supported."),
    };
}
