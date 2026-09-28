using Marshal.Abstractions.Models;
using Marshal.Abstractions.Secrets;

namespace Marshal.Core.Models;

/// <summary>
/// One configured model (<c>models.&lt;key&gt;</c> in <c>marshal.yaml</c>). Changes need a restart.
/// </summary>
public sealed class ModelOptions
{
    public ModelTier Tier { get; set; }

    /// <summary><c>ollama</c>, <c>openai-compatible</c> or <c>anthropic</c>.</summary>
    public string Provider { get; set; } = "";

    /// <summary>Provider model name (e.g. <c>qwen3:8b</c>, <c>claude-sonnet-4-5</c>).</summary>
    public string Model { get; set; } = "";

    /// <summary>Server URL (Ollama, OpenAI-compatible servers).</summary>
    public Uri? Endpoint { get; set; }

    /// <summary>Name of the secret holding the API key.</summary>
    public string? ApiKeySecret { get; set; }

    /// <summary>The API key, resolved from the secret store at startup.</summary>
    [Secret(nameof(ApiKeySecret))]
    public string? ApiKey { get; set; }

    public bool Vision { get; set; }

    public bool Tools { get; set; } = true;

    public int ContextLength { get; set; } = 8192;

    /// <summary>Price table entry; models without one cost 0.</summary>
    public ModelPrice? Price { get; set; }
}
