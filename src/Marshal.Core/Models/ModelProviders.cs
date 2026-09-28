namespace Marshal.Core.Models;

/// <summary>
/// Supported provider kinds.
/// </summary>
public static class ModelProviders
{
    public const string Ollama = "ollama";
    public const string OpenAiCompatible = "openai-compatible";
    public const string Anthropic = "anthropic";

    public static IReadOnlyList<string> All { get; } = [Ollama, OpenAiCompatible, Anthropic];
}
