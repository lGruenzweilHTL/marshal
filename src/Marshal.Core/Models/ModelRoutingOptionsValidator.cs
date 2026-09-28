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
            if (model.ContextLength <= 0)
                failures.Add($"{path}.contextLength must be positive.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
