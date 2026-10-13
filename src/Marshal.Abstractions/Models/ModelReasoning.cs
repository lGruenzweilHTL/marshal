namespace Marshal.Abstractions.Models;

/// <summary>
/// Reasoning effort a model is asked for on text turns and background runs (<c>models.&lt;key&gt;.reasoning</c>).
/// </summary>
public enum ModelReasoning
{
    Off,

    Low,

    Medium,

    High,
}
