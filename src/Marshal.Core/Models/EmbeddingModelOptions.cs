using Marshal.Abstractions.Secrets;

namespace Marshal.Core.Models;

/// <summary>
/// The embedding model (<c>embedding</c> in <c>marshal.yaml</c>): multilingual, used for memory. Changes need a
/// restart, which re-embeds memory.
/// </summary>
public sealed class EmbeddingModelOptions
{
    /// <summary><c>ollama</c> or <c>openai-compatible</c> (Anthropic offers no embeddings).</summary>
    public string Provider { get; set; } = "";

    /// <summary>Provider model name (e.g. <c>bge-m3</c>).</summary>
    public string Model { get; set; } = "";

    /// <summary>Server URL.</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Name of the secret holding the API key, if the server needs one.</summary>
    public string? ApiKeySecret { get; set; }

    /// <summary>The API key, resolved from the secret store at startup.</summary>
    [Secret(nameof(ApiKeySecret))]
    public string? ApiKey { get; set; }
}
