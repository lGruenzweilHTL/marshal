using Marshal.Api.Models;
using Marshal.Core.Models;
using Microsoft.Extensions.AI;

namespace Marshal.Api.Tests.Models;

public sealed class ProviderEmbeddingGeneratorFactoryTests
{
    [Theory]
    [InlineData("ollama", null)]
    [InlineData("openai-compatible", "http://llama:8080/v1")]
    public void Create_SupportedProvider_GeneratorForTheModel(string provider, string? endpoint)
    {
        using IEmbeddingGenerator<string, Embedding<float>> generator = new ProviderEmbeddingGeneratorFactory().Create(
            new EmbeddingModelOptions { Provider = provider, Model = "bge-m3", Endpoint = endpoint is null ? null : new Uri(endpoint) });

        Assert.Equal("bge-m3", generator.GetService<EmbeddingGeneratorMetadata>()?.DefaultModelId);
    }

    [Fact]
    public void Create_Anthropic_NotSupported()
    {
        var factory = new ProviderEmbeddingGeneratorFactory();

        Assert.Throws<InvalidOperationException>(() => factory.Create(new EmbeddingModelOptions { Provider = "anthropic", Model = "claude" }));
    }
}
