using System.ClientModel;
using Anthropic;
using Marshal.Core.Models;
using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;

namespace Marshal.Api.Models;

/// <summary>
/// Creates provider clients. The only place that knows the provider SDKs.
/// </summary>
internal sealed class ProviderChatClientFactory : IChatClientFactory
{
    private static readonly Uri _defaultOllama = new("http://localhost:11434");

    public IChatClient Create(string key, ModelOptions options) => options.Provider switch
    {
        ModelProviders.Ollama => new OllamaApiClient(options.Endpoint ?? _defaultOllama, options.Model),
        ModelProviders.OpenAiCompatible => new OpenAIClient(
                new ApiKeyCredential(options.ApiKey ?? "none"),
                new OpenAIClientOptions { Endpoint = options.Endpoint })
            .GetChatClient(options.Model)
            .AsIChatClient(),
        ModelProviders.Anthropic => CreateAnthropic(options),
        _ => throw new InvalidOperationException($"models.{key}.provider '{options.Provider}' is not supported."),
    };

    private static IChatClient CreateAnthropic(ModelOptions options)
    {
        var client = new AnthropicClient
        {
            ApiKey = options.ApiKey ?? throw new InvalidOperationException("Anthropic API key was not resolved."),
            BaseUrl = options.Endpoint?.ToString() ?? "https://api.anthropic.com",
        };
        return client.AsIChatClient(options.Model);
    }
}
