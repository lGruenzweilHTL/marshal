using Marshal.Abstractions.Models;
using Marshal.Core.Models;
using Microsoft.Extensions.Options;

namespace Marshal.Core.Tests.Models;

public sealed class ModelRoutingOptionsValidatorTests
{
    private readonly ModelRoutingOptionsValidator _validator = new();

    [Fact]
    public void Validate_OnePerProvider_Succeeds()
    {
        ValidateOptionsResult result = Validate(
            ("fast", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen3" }),
            ("local", new ModelOptions { Tier = ModelTier.Standard, Provider = "openai-compatible", Model = "q", Endpoint = new Uri("http://llama:8080/v1") }),
            ("claude", new ModelOptions { Tier = ModelTier.Advisor, Provider = "anthropic", Model = "claude", ApiKeySecret = "anthropic_key" }));

        Assert.True(result.Succeeded);
    }

    [Fact]
    public void Validate_UnknownReasoning_NamesKey()
    {
        ValidateOptionsResult result = Validate(("fast", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "qwen3", Reasoning = "extreme" }));

        Assert.True(result.Failed);
        Assert.Contains("models.fast.reasoning must be one of off, low, medium, high.", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("openai", "models.bad.provider")]
    [InlineData("", "models.bad.provider")]
    public void Validate_UnknownProvider_NamesKey(string provider, string key)
    {
        ValidateOptionsResult result = Validate(("bad", new ModelOptions { Tier = ModelTier.Fast, Provider = provider, Model = "m" }));

        Assert.True(result.Failed);
        Assert.Contains(key, result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AnthropicWithoutKeyReference_NamesKey()
    {
        ValidateOptionsResult result = Validate(("claude", new ModelOptions { Tier = ModelTier.Standard, Provider = "anthropic", Model = "claude" }));

        Assert.Contains("models.claude.apiKeySecret", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_OpenAiCompatibleWithoutEndpoint_NamesKey()
    {
        ValidateOptionsResult result = Validate(("local", new ModelOptions { Tier = ModelTier.Standard, Provider = "openai-compatible", Model = "q" }));

        Assert.Contains("models.local.endpoint", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_MissingModelName_NamesKey()
    {
        ValidateOptionsResult result = Validate(("fast", new ModelOptions { Tier = ModelTier.Fast, Provider = "ollama", Model = "" }));

        Assert.Contains("models.fast.model", result.FailureMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_MissingEmbeddingModel_Fails()
    {
        ValidateOptionsResult result = _validator.Validate(null, new ModelRoutingOptions());

        Assert.Contains("embedding.provider", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("anthropic", null, "embedding.provider")]
    [InlineData("openai-compatible", null, "embedding.endpoint")]
    [InlineData("ollama", "", "embedding.model")]
    public void Validate_InvalidEmbeddingModel_NamesKey(string provider, string? model, string key)
    {
        var options = new ModelRoutingOptions { Embedding = new EmbeddingModelOptions { Provider = provider, Model = model ?? "bge-m3" } };

        Assert.Contains(key, _validator.Validate(null, options).FailureMessage, StringComparison.Ordinal);
    }

    private ValidateOptionsResult Validate(params (string Key, ModelOptions Options)[] models)
    {
        var options = new ModelRoutingOptions { Embedding = new EmbeddingModelOptions { Provider = "ollama", Model = "bge-m3" } };
        foreach ((string key, ModelOptions model) in models)
            options.Models[key] = model;

        return _validator.Validate(null, options);
    }
}
