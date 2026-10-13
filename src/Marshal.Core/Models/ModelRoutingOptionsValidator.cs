using Microsoft.Extensions.Options;

namespace Marshal.Core.Models;

/// <summary>
/// Validates the model configuration at startup; every message names the offending key.
/// </summary>
public sealed class ModelRoutingOptionsValidator : IValidateOptions<ModelRoutingOptions>
{
    public ValidateOptionsResult Validate(string? name, ModelRoutingOptions options)
    {
        var failures = new List<string>();
        foreach ((string key, ModelOptions model) in options.Models)
        {
            string path = $"models.{key}";
            if (!ModelProviders.All.Contains(model.Provider))
                failures.Add($"{path}.provider must be one of {string.Join(", ", ModelProviders.All)}.");
            if (string.IsNullOrWhiteSpace(model.Model))
                failures.Add($"{path}.model is required.");
            if (model.Provider == ModelProviders.Anthropic && string.IsNullOrWhiteSpace(model.ApiKeySecret))
                failures.Add($"{path}.apiKeySecret is required for anthropic models.");
            if (model.Provider == ModelProviders.OpenAiCompatible && model.Endpoint is null)
                failures.Add($"{path}.endpoint is required for openai-compatible models.");
            if (!ModelReasoningSupport.TryParse(model.Reasoning, out _))
                failures.Add($"{path}.reasoning must be one of off, low, medium, high.");
            if (model.ContextLength <= 0)
                failures.Add($"{path}.contextLength must be positive.");
        }

        EmbeddingModelOptions embedding = options.Embedding ?? new();
        if (embedding.Provider is not (ModelProviders.Ollama or ModelProviders.OpenAiCompatible))
            failures.Add($"embedding.provider must be {ModelProviders.Ollama} or {ModelProviders.OpenAiCompatible}.");
        if (string.IsNullOrWhiteSpace(embedding.Model))
            failures.Add("embedding.model is required.");
        if (embedding.Provider == ModelProviders.OpenAiCompatible && embedding.Endpoint is null)
            failures.Add("embedding.endpoint is required for openai-compatible models.");

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
