using System.ComponentModel.DataAnnotations;

namespace Marshal.Core.Agent;

/// <summary>
/// Agent loop limits (<c>agent</c> in <c>marshal.yaml</c>).
/// </summary>
public sealed class AgentOptions
{
    /// <summary>Maximum tool rounds per run.</summary>
    [Range(1, 50)]
    public int MaxSteps { get; set; } = 8;

    /// <summary>Hard latency cap for voice turns.</summary>
    public TimeSpan VoiceLatencyCap { get; set; } = TimeSpan.FromSeconds(8);

    /// <summary>Hard latency cap for text turns.</summary>
    public TimeSpan TextLatencyCap { get; set; } = TimeSpan.FromSeconds(60);

    public AdvisorOptions Advisor { get; set; } = new();
}
