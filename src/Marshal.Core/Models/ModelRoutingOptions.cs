namespace Marshal.Core.Models;

/// <summary>
/// The configured models, keyed by a name of the operator's choice.
/// </summary>
public sealed class ModelRoutingOptions
{
    public Dictionary<string, ModelOptions> Models { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The embedding model (required: memory depends on it).</summary>
    public EmbeddingModelOptions? Embedding { get; set; }
}
